using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Core.Services;
using Gannet.Providers.Http;
using Gannet.Providers.Models;
using Gannet.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gannet.Providers.Tests.Models;

public class OpenRouterDeepSeekTests
{
    private const string Secret = "sk-or-super-secret-value";

    private const string ChatReply = """
        { "id": "c1", "object": "chat.completion", "created": 1761120000, "model": "m",
          "choices": [ { "index": 0, "message": { "role": "assistant", "content": "hi" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 } }
        """;

    // Hand-written from the documented shape of GET /api/v1/models (openrouter.ai/docs/api-reference/models/get-models).
    private const string ModelList = """
        { "data": [
            { "id": "other/model", "context_length": 1111, "top_provider": { "context_length": 1111 }, "supported_parameters": [ "tools" ] },
            { "id": "acme/big", "context_length": 200000, "top_provider": { "context_length": 128000, "max_completion_tokens": 4096 }, "supported_parameters": [ "temperature", "tools", "tool_choice" ] },
            { "id": "acme/plain", "context_length": 64000, "top_provider": null, "supported_parameters": [ "temperature", "top_p" ] },
            { "id": "acme/no-top", "context_length": 32000, "supported_parameters": [ "temperature" ] } ] }
        """;

    private sealed class StubClientFactory(StubHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => handler.Client();
    }

    private static ChatClientFactory Factory(Dictionary<string, ModelOptions> models, StubHttpHandler handler) =>
        new(Options.Create(new GannetOptions { Models = new(models, StringComparer.OrdinalIgnoreCase) }), new StubClientFactory(handler));

    private static StubHttpHandler Routed(Func<HttpRequestMessage, HttpResponseMessage?>? models = null) =>
        new(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
            ? models?.Invoke(r) ?? StubHttpHandler.Json(ModelList)
            : StubHttpHandler.Json(ChatReply));

    private static Dictionary<string, ModelOptions> OpenRouter(string model = "acme/big", string? key = Secret, string? endpoint = null, int? window = null, bool? tools = null) =>
        new() { ["or"] = new ModelOptions { Provider = "OpenRouter", Model = model, ApiKey = key, Endpoint = endpoint, ContextWindow = window, SupportsTools = tools } };

    private static RecordedRequest Chat(StubHttpHandler handler) =>
        handler.Requests.Single(r => r.Method == HttpMethod.Post);

    [Theory]
    [InlineData("OpenRouter", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("DeepSeek", "https://api.deepseek.com/chat/completions")]
    public async Task A_provider_builds_a_client_against_its_documented_default_endpoint(string provider, string chatUrl)
    {
        var handler = Routed();
        var factory = Factory(new() { ["m"] = new ModelOptions { Provider = provider, Model = "deepseek-flash", ApiKey = Secret, ContextWindow = 4096, SupportsTools = true } }, handler);

        var handle = await factory.GetAsync("m", CancellationToken.None);
        await handle.Client.GetResponseAsync("hi");

        var request = Chat(handler);
        Assert.Equal(chatUrl, request.Uri.ToString());
        Assert.Equal($"Bearer {Secret}", request.Headers["Authorization"]);
        Assert.Equal("deepseek-flash", JsonDocument.Parse(request.Body!).RootElement.GetProperty("model").GetString());
        Assert.All(handler.Requests, r => Assert.DoesNotContain(Secret, r.Uri.ToString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("OpenRouter", "https://proxy.local/or/v1", "https://proxy.local/or/v1/chat/completions")]
    [InlineData("DeepSeek", "https://proxy.local/ds", "https://proxy.local/ds/chat/completions")]
    public async Task An_endpoint_in_configuration_overrides_the_default(string provider, string endpoint, string chatUrl)
    {
        var handler = Routed();
        var factory = Factory(new() { ["m"] = new ModelOptions { Provider = provider, Model = "x", ApiKey = Secret, Endpoint = endpoint, ContextWindow = 4096, SupportsTools = true } }, handler);

        await (await factory.GetAsync("m", CancellationToken.None)).Client.GetResponseAsync("hi");

        Assert.Equal(chatUrl, Chat(handler).Uri.ToString());
    }

    [Theory]
    [InlineData("OpenRouter", null)]
    [InlineData("OpenRouter", "")]
    [InlineData("OpenRouter", "   ")]
    [InlineData("DeepSeek", null)]
    [InlineData("DeepSeek", "")]
    [InlineData("DeepSeek", "   ")]
    public async Task A_missing_or_blank_key_names_the_config_path_and_sends_nothing(string provider, string? key)
    {
        var handler = Routed();
        var factory = Factory(new() { ["m1"] = new ModelOptions { Provider = provider, Model = "x", ApiKey = key } }, handler);

        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => factory.GetAsync("m1", CancellationToken.None));

        Assert.Contains("Gannet:Models:m1:ApiKey", error.Message, StringComparison.Ordinal);
        Assert.Equal(provider, error.Service);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_provider_error_lists_both_new_providers()
    {
        var factory = Factory(new() { ["x"] = new ModelOptions { Provider = "Bard", Model = "m" } }, Routed());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.GetAsync("x", CancellationToken.None));

        Assert.Contains("Ollama, OpenAI, OpenAICompatible, AzureOpenAI, Anthropic, Gemini, OpenRouter, DeepSeek", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_probe_prefers_top_provider_context_over_context_length_and_reads_tools()
    {
        var handler = Routed();
        var factory = Factory(OpenRouter("acme/big"), handler);

        var handle = await factory.GetAsync("or", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(128000, true), handle.Capabilities);
        var probe = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, probe.Method);
        Assert.Equal("https://openrouter.ai/api/v1/models", probe.Uri.ToString());
    }

    [Theory]
    [InlineData("acme/plain", 64000, false)]
    [InlineData("acme/no-top", 32000, false)]
    public async Task The_probe_falls_back_to_context_length_and_no_tools_without_the_tools_parameter(string model, int window, bool tools)
    {
        var factory = Factory(OpenRouter(model), Routed());

        Assert.Equal(new ModelCapabilities(window, tools), (await factory.GetAsync("or", CancellationToken.None)).Capabilities);
    }

    [Fact]
    public async Task The_probe_sends_the_key_as_a_bearer_header_and_never_in_the_url()
    {
        var handler = Routed();

        await Factory(OpenRouter(), handler).GetAsync("or", CancellationToken.None);

        var probe = Assert.Single(handler.Requests);
        Assert.Equal($"Bearer {Secret}", probe.Headers["Authorization"]);
        Assert.DoesNotContain(Secret, probe.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_probe_goes_to_the_configured_endpoint()
    {
        var handler = Routed();

        await Factory(OpenRouter(endpoint: "https://proxy.local/or/v1/"), handler).GetAsync("or", CancellationToken.None);

        Assert.Equal("https://proxy.local/or/v1/models", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task A_401_falls_back_to_8192_and_no_tools_without_leaking_the_key()
    {
        var handler = Routed(_ => StubHttpHandler.Json($$"""{ "error": { "code": 401, "message": "bad key {{Secret}}" } }""", HttpStatusCode.Unauthorized));

        var handle = await Factory(OpenRouter(), handler).GetAsync("or", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(8192, false), handle.Capabilities);
        Assert.DoesNotContain(Secret, handle.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_falls_back_to_8192_and_no_tools()
    {
        var handler = Routed(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        var handle = await Factory(OpenRouter(), handler).GetAsync("or", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(8192, false), handle.Capabilities);
    }

    [Theory]
    [InlineData("not/listed")]
    public async Task A_model_the_list_does_not_have_falls_back_to_8192_and_no_tools(string model)
    {
        var handle = await Factory(OpenRouter(model), Routed()).GetAsync("or", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(8192, false), handle.Capabilities);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "data": [ { "id": "acme/big", "context_length": "lots", "supported_parameters": "tools" } ] }""")]
    [InlineData("""{ "data": [ { "id": "acme/big", "context_length": -5 } ] }""")]
    public async Task An_answer_that_is_not_the_documented_shape_falls_back(string body)
    {
        var handle = await Factory(OpenRouter(), Routed(_ => StubHttpHandler.Json(body))).GetAsync("or", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(8192, false), handle.Capabilities);
    }

    [Fact]
    public async Task Configuration_overrides_the_probe_and_a_fully_configured_model_is_not_probed()
    {
        var handler = Routed();
        var factory = Factory(new()
        {
            ["both"] = new ModelOptions { Provider = "OpenRouter", Model = "acme/big", ApiKey = Secret, ContextWindow = 16384, SupportsTools = false },
            ["window"] = new ModelOptions { Provider = "OpenRouter", Model = "acme/big", ApiKey = Secret, ContextWindow = 16384 },
            ["tools"] = new ModelOptions { Provider = "OpenRouter", Model = "acme/plain", ApiKey = Secret, SupportsTools = true },
        }, handler);

        Assert.Equal(new ModelCapabilities(16384, false), (await factory.GetAsync("both", CancellationToken.None)).Capabilities);
        Assert.Empty(handler.Requests);

        Assert.Equal(new ModelCapabilities(16384, true), (await factory.GetAsync("window", CancellationToken.None)).Capabilities);
        Assert.Equal(new ModelCapabilities(64000, true), (await factory.GetAsync("tools", CancellationToken.None)).Capabilities);
    }

    [Fact]
    public async Task A_failed_probe_is_not_cached_and_a_successful_one_is()
    {
        var fail = true;
        var handler = Routed(_ => fail ? StubHttpHandler.Json("{}", HttpStatusCode.ServiceUnavailable) : null);
        var factory = Factory(OpenRouter(), handler);

        Assert.Equal(new ModelCapabilities(8192, false), (await factory.GetAsync("or", CancellationToken.None)).Capabilities);
        Assert.Equal(new ModelCapabilities(8192, false), (await factory.GetAsync("or", CancellationToken.None)).Capabilities);
        Assert.Equal(2, handler.Requests.Count);

        fail = false;
        var first = await factory.GetAsync("or", CancellationToken.None);
        var second = await factory.GetAsync("or", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(128000, true), first.Capabilities);
        Assert.Same(first, second);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_model_missing_from_the_list_is_not_cached()
    {
        var listed = false;
        var handler = Routed(_ => listed ? null : StubHttpHandler.Json("""{ "data": [] }"""));
        var factory = Factory(OpenRouter(), handler);

        Assert.Equal(8192, (await factory.GetAsync("or", CancellationToken.None)).Capabilities.ContextWindow);

        listed = true;
        Assert.Equal(128000, (await factory.GetAsync("or", CancellationToken.None)).Capabilities.ContextWindow);
    }

    [Fact]
    public async Task Chat_requests_to_openrouter_carry_the_attribution_headers_and_the_probe_does_not_need_them()
    {
        var handler = Routed();

        await (await Factory(OpenRouter(), handler).GetAsync("or", CancellationToken.None)).Client.GetResponseAsync("hi");

        var chat = Chat(handler);
        Assert.Equal("https://github.com/dbhq-uk/gannet", chat.Headers["HTTP-Referer"]);
        Assert.Equal("gannet", chat.Headers["X-OpenRouter-Title"]);
    }

    [Fact]
    public async Task Deepseek_chat_requests_carry_no_openrouter_headers()
    {
        var handler = Routed();
        var factory = Factory(new() { ["d"] = new ModelOptions { Provider = "DeepSeek", Model = "deepseek-flash", ApiKey = Secret } }, handler);

        await (await factory.GetAsync("d", CancellationToken.None)).Client.GetResponseAsync("hi");

        var chat = Chat(handler);
        Assert.False(chat.Headers.ContainsKey("HTTP-Referer"));
        Assert.False(chat.Headers.ContainsKey("X-OpenRouter-Title"));
    }

    [Theory]
    [InlineData("anthropic/claude-sonnet-5-5")]
    [InlineData("openai/gpt-5.5")]
    public async Task Openrouter_models_use_the_openai_wire_format_and_send_no_anthropic_thinking(string model)
    {
        var handler = Routed();
        var factory = Factory(OpenRouter(model, window: 100000, tools: true), handler);

        await (await factory.GetAsync("or", CancellationToken.None)).Client
            .GetResponseAsync("hi", new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium } });

        var chat = Chat(handler);
        Assert.EndsWith("/chat/completions", chat.Uri.AbsolutePath, StringComparison.Ordinal);
        var body = JsonDocument.Parse(chat.Body!).RootElement;
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.Equal(model, body.GetProperty("model").GetString());
    }

    private static readonly ChatOptions SchemaOptions = new()
    {
        ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonDocument.Parse("""{ "type": "object", "properties": { "a": { "type": "string" } } }""").RootElement, "answer"),
    };

    [Fact]
    public async Task Deepseek_gets_json_object_mode_even_when_a_schema_was_asked_for()
    {
        var handler = Routed();
        var factory = Factory(new() { ["d"] = new ModelOptions { Provider = "DeepSeek", Model = "deepseek-flash", ApiKey = Secret } }, handler);

        await (await factory.GetAsync("d", CancellationToken.None)).Client.GetResponseAsync("hi", SchemaOptions);

        var body = JsonDocument.Parse(Chat(handler).Body!).RootElement;
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.DoesNotContain("json_schema", Chat(handler).Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deepseek_keeps_plain_json_mode_and_leaves_the_callers_options_alone()
    {
        var handler = Routed();
        var factory = Factory(new() { ["d"] = new ModelOptions { Provider = "DeepSeek", Model = "deepseek-flash", ApiKey = Secret } }, handler);
        var mine = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };

        var client = (await factory.GetAsync("d", CancellationToken.None)).Client;
        await client.GetResponseAsync("hi", mine);
        await client.GetResponseAsync("hi", SchemaOptions);

        Assert.Equal("json_object", JsonDocument.Parse(handler.Requests.First(r => r.Method == HttpMethod.Post).Body!).RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Same(ChatResponseFormat.Json, mine.ResponseFormat);
        Assert.IsType<ChatResponseFormatJson>(SchemaOptions.ResponseFormat);
        Assert.NotNull(((ChatResponseFormatJson)SchemaOptions.ResponseFormat!).Schema);
    }

    [Theory]
    [InlineData("OpenRouter")]
    [InlineData("OpenAI")]
    public async Task Other_providers_still_send_the_json_schema(string provider)
    {
        var handler = Routed();
        var factory = Factory(new() { ["o"] = new ModelOptions { Provider = provider, Model = "acme/big", ApiKey = Secret, ContextWindow = 4096, SupportsTools = true } }, handler);

        await (await factory.GetAsync("o", CancellationToken.None)).Client.GetResponseAsync("hi", SchemaOptions);

        var format = JsonDocument.Parse(Chat(handler).Body!).RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("answer", format.GetProperty("json_schema").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("deepseek-v4-pro", 1_000_000, true)]
    [InlineData("deepseek-flash", 1_000_000, true)]
    [InlineData("deepseek-v4-flash", 1_000_000, true)]
    [InlineData("deepseek-v4-flash-vision-exp", 1_000_000, true)]
    [InlineData("deepseek-v9-future", 8192, false)]
    [InlineData("deepseek-chat", 8192, false)]
    public async Task Deepseek_uses_the_documented_table_and_falls_back_for_unknown_ids(string model, int window, bool tools)
    {
        var handler = Routed();
        var factory = Factory(new() { ["d"] = new ModelOptions { Provider = "DeepSeek", Model = model, ApiKey = Secret } }, handler);

        var handle = await factory.GetAsync("d", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(window, tools), handle.Capabilities);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Deepseek_configuration_overrides_the_table()
    {
        var factory = Factory(new() { ["d"] = new ModelOptions { Provider = "DeepSeek", Model = "deepseek-flash", ApiKey = Secret, ContextWindow = 50000, SupportsTools = false } }, Routed());

        Assert.Equal(new ModelCapabilities(50000, false), (await factory.GetAsync("d", CancellationToken.None)).Capabilities);
    }

    [Fact]
    public async Task Openrouter_ignores_the_table_so_a_bare_name_it_does_not_list_gets_the_fallback()
    {
        var factory = Factory(OpenRouter("gpt-5.5"), Routed());

        Assert.Equal(new ModelCapabilities(8192, false), (await factory.GetAsync("or", CancellationToken.None)).Capabilities);
    }

    [Fact]
    public async Task A_body_over_the_cap_falls_back()
    {
        var handler = Routed(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new RepeatingStream(JsonRead.MaxBodyBytes + 1024)) });

        var handle = await Factory(OpenRouter(), handler).GetAsync("or", CancellationToken.None);

        Assert.Equal(new ModelCapabilities(8192, false), handle.Capabilities);
    }

    [Fact]
    public async Task A_body_that_stalls_after_the_headers_gives_null_at_the_deadline_not_at_the_callers_patience()
    {
        var handler = Routed(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await OpenRouterModelInfo.ReadAsync(handler.Client(), new Uri("https://openrouter.ai/api/v1"), "acme/big", Secret, CancellationToken.None, TimeSpan.FromMilliseconds(200));

        Assert.Null(result);
        // Wide on purpose: without the deadline the read never ends, so any bound still catches the fault.
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(60), $"took {started.Elapsed}");
    }

    private sealed class RepeatingStream(long length) : Stream
    {
        private long position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, length - position);
            buffer[..n].Fill((byte)' ');
            position += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task No_handle_prints_a_key()
    {
        var handle = await Factory(OpenRouter(), Routed()).GetAsync("or", CancellationToken.None);

        Assert.DoesNotContain(Secret, handle.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, handle.Client.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_model_chat_http_client_has_no_timeout_of_its_own()
    {
        var services = new ServiceCollection();
        services.AddGannetProviders(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.ModelChat);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}
