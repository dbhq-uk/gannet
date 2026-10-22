using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Core.Services;
using Gannet.Providers.Models;
using Gannet.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Gannet.Providers.Tests.Models;

public class ChatClientFactoryTests
{
    private const string Secret = "sk-super-secret-value";

    private sealed class OneClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CapturingClient : IChatClient
    {
        public ChatOptions? Seen { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Seen = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Seen = options;
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static ChatClientFactory Factory(Dictionary<string, ModelOptions> models, StubHttpHandler? handler = null) =>
        new(Options.Create(new GannetOptions { Models = new(models, StringComparer.OrdinalIgnoreCase) }),
            new OneClientFactory((handler ?? new StubHttpHandler(_ => StubHttpHandler.Json("{}"))).Client()));

    [Fact]
    public async Task Ollama_context_is_capped_at_32k_unless_configured()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "capabilities": ["tools"], "model_info": { "general.architecture": "qwen3", "qwen3.context_length": 262144 } }"""));
        var factory = Factory(new()
        {
            ["local"] = new ModelOptions { Provider = "Ollama", Model = "qwen3:4b" },
            ["local-big"] = new ModelOptions { Provider = "Ollama", Model = "qwen3:4b", ContextWindow = 65536 },
        }, handler);

        Assert.Equal(32768, (await factory.GetAsync("local", CancellationToken.None)).Capabilities.ContextWindow);
        Assert.Equal(65536, (await factory.GetAsync("local-big", CancellationToken.None)).Capabilities.ContextWindow);
        Assert.True((await factory.GetAsync("local", CancellationToken.None)).Capabilities.CanRunAgentMode);
    }

    [Fact]
    public async Task Ollama_below_the_cap_uses_the_trained_context()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "capabilities": [], "model_info": { "general.architecture": "llama", "llama.context_length": 8192 } }"""));
        var factory = Factory(new() { ["local"] = new ModelOptions { Provider = "Ollama", Model = "m", SupportsTools = true } }, handler);

        var handle = await factory.GetAsync("local", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(8192, true), handle.Capabilities);
    }

    [Fact]
    public async Task Num_ctx_is_set_on_every_request_without_changing_the_callers_options()
    {
        var inner = new CapturingClient();
        var client = ChatClientFactory.WithNumCtx(inner, 24576);
        var mine = new ChatOptions { Temperature = 0.2f };

        await client.GetResponseAsync("hi", mine);
        Assert.Equal(24576, inner.Seen!.AdditionalProperties!["num_ctx"]);
        Assert.Equal(0.2f, inner.Seen.Temperature);
        Assert.Null(mine.AdditionalProperties);

        await client.GetResponseAsync("hi");
        Assert.Equal(24576, inner.Seen!.AdditionalProperties!["num_ctx"]);

        await foreach (var _ in client.GetStreamingResponseAsync("hi"))
        {
        }

        Assert.Equal(24576, inner.Seen!.AdditionalProperties!["num_ctx"]);
    }

    [Fact]
    public async Task Cloud_models_use_the_table_and_overrides()
    {
        var factory = Factory(new()
        {
            ["sonnet"] = new ModelOptions { Provider = "Anthropic", Model = "claude-sonnet-5-5", ApiKey = "k" },
            ["haiku-old"] = new ModelOptions { Provider = "Anthropic", Model = "claude-haiku-4-5", ApiKey = "k" },
            ["mystery"] = new ModelOptions { Provider = "OpenAICompatible", Endpoint = "http://localhost:8080/v1", Model = "something-local" },
            ["mystery-tools"] = new ModelOptions { Provider = "OpenAICompatible", Endpoint = "http://localhost:8080/v1", Model = "something-local", ContextWindow = 32768, SupportsTools = true },
        });

        Assert.Equal(new ModelCapabilities(1_000_000, true), (await factory.GetAsync("sonnet", CancellationToken.None)).Capabilities);
        Assert.Equal(new ModelCapabilities(200_000, true), (await factory.GetAsync("haiku-old", CancellationToken.None)).Capabilities);
        Assert.Equal(new ModelCapabilities(8192, false), (await factory.GetAsync("mystery", CancellationToken.None)).Capabilities);
        Assert.True((await factory.GetAsync("mystery-tools", CancellationToken.None)).Capabilities.CanRunAgentMode);
    }

    [Fact]
    public async Task Every_provider_builds_a_client()
    {
        var factory = Factory(new()
        {
            ["a"] = new ModelOptions { Provider = "OpenAI", Model = "gpt-5.4", ApiKey = "k" },
            ["b"] = new ModelOptions { Provider = "AzureOpenAI", Endpoint = "https://r.openai.azure.com/openai/v1/", Model = "dep", ApiKey = "k" },
            ["c"] = new ModelOptions { Provider = "Anthropic", Model = "claude-opus-5-5", ApiKey = "k" },
            ["d"] = new ModelOptions { Provider = "Gemini", Model = "gemini-3.6-flash", ApiKey = "k" },
            ["e"] = new ModelOptions { Provider = "openaicompatible", Endpoint = "http://localhost:8080/v1", Model = "m", ContextWindow = 4096 },
        });

        foreach (var name in new[] { "a", "b", "c", "d", "e" })
        {
            Assert.NotNull((await factory.GetAsync(name, CancellationToken.None)).Client);
        }
    }

    [Fact]
    public async Task A_vllm_server_supplies_the_context_when_configuration_does_not()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.AbsolutePath == "/v1/models"
            ? StubHttpHandler.Json("""{ "data": [ { "id": "qwen3-32b", "max_model_len": 40960 } ] }""")
            : StubHttpHandler.Json("{}", HttpStatusCode.NotFound));
        var factory = Factory(new()
        {
            ["vllm"] = new ModelOptions { Provider = "OpenAICompatible", Endpoint = "http://spark:8000/v1", Model = "qwen3-32b", SupportsTools = true },
            ["pinned"] = new ModelOptions { Provider = "OpenAICompatible", Endpoint = "http://spark:8000/v1", Model = "qwen3-32b", ContextWindow = 16384 },
        }, handler);

        var vllm = await factory.GetAsync("vllm", CancellationToken.None);
        Assert.Equal(new ModelCapabilities(40960, true), vllm.Capabilities);

        var requestsBefore = handler.Requests.Count;
        Assert.Equal(16384, (await factory.GetAsync("pinned", CancellationToken.None)).Capabilities.ContextWindow);
        Assert.Equal(requestsBefore, handler.Requests.Count);
    }

    [Fact]
    public async Task A_server_that_answers_neither_shape_falls_back_to_8192_and_no_tools()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{}", HttpStatusCode.NotFound));
        var factory = Factory(new() { ["srv"] = new ModelOptions { Provider = "OpenAICompatible", Endpoint = "http://spark:8000/v1", Model = "x" } }, handler);

        Assert.Equal(new ModelCapabilities(8192, false), (await factory.GetAsync("srv", CancellationToken.None)).Capabilities);
    }

    [Fact]
    public async Task A_successful_probe_is_cached_and_a_failed_one_is_not()
    {
        var fail = true;
        var handler = new StubHttpHandler(_ => fail
            ? StubHttpHandler.Json("{}", HttpStatusCode.ServiceUnavailable)
            : StubHttpHandler.Json("""{ "capabilities": ["tools"], "model_info": { "general.architecture": "a", "a.context_length": 20000 } }"""));
        var factory = Factory(new() { ["local"] = new ModelOptions { Provider = "Ollama", Model = "m" } }, handler);

        await Assert.ThrowsAsync<ServiceFailedException>(() => factory.GetAsync("local", CancellationToken.None));
        await Assert.ThrowsAsync<ServiceFailedException>(() => factory.GetAsync("local", CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);

        fail = false;
        var first = await factory.GetAsync("local", CancellationToken.None);
        var second = await factory.GetAsync("local", CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(20000, first.Capabilities.ContextWindow);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("AzureOpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Gemini")]
    public async Task A_keyed_provider_with_no_key_names_the_config_path(string provider)
    {
        var factory = Factory(new() { ["m1"] = new ModelOptions { Provider = provider, Endpoint = "https://r.openai.azure.com/openai/v1/", Model = "x" } });

        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => factory.GetAsync("m1", CancellationToken.None));

        Assert.Contains("Gannet:Models:m1:ApiKey", error.Message, StringComparison.Ordinal);
        Assert.Equal(provider, error.Service);
    }

    [Fact]
    public async Task Compatible_and_azure_models_without_an_endpoint_are_clear_errors()
    {
        var factory = Factory(new()
        {
            ["c"] = new ModelOptions { Provider = "OpenAICompatible", Model = "x" },
            ["z"] = new ModelOptions { Provider = "AzureOpenAI", Model = "x", ApiKey = "k" },
        });

        Assert.Contains("Endpoint", (await Assert.ThrowsAsync<InvalidOperationException>(() => factory.GetAsync("c", CancellationToken.None))).Message, StringComparison.Ordinal);
        Assert.Contains("endpoint", (await Assert.ThrowsAsync<InvalidOperationException>(() => factory.GetAsync("z", CancellationToken.None))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_model_and_unknown_provider_are_clear_errors()
    {
        var factory = Factory(new() { ["x"] = new ModelOptions { Provider = "Bard", Model = "m" } });
        await Assert.ThrowsAsync<UnknownServiceException>(() => factory.GetAsync("nope", CancellationToken.None));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.GetAsync("x", CancellationToken.None));
        Assert.Contains("Ollama, OpenAI, OpenAICompatible, AzureOpenAI, Anthropic, Gemini", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_models_without_keys()
    {
        var factory = Factory(new() { ["sonnet"] = new ModelOptions { Provider = "Anthropic", Model = "claude-sonnet-5-5", ApiKey = Secret } });
        var descriptor = Assert.Single(factory.Models);
        Assert.Equal(("sonnet", "Anthropic", "claude-sonnet-5-5"), (descriptor.Name, descriptor.Provider, descriptor.Model));
        Assert.DoesNotContain(Secret, descriptor.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handle_never_prints_a_key()
    {
        var factory = Factory(new() { ["sonnet"] = new ModelOptions { Provider = "Anthropic", Model = "claude-sonnet-5-5", ApiKey = Secret } });
        var handle = await factory.GetAsync("sonnet", CancellationToken.None);
        Assert.DoesNotContain(Secret, handle.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, handle.Name, StringComparison.Ordinal);
    }
}
