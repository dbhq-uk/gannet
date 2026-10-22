using System.ClientModel.Primitives;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Providers.Models;
using Gannet.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Gannet.Providers.Tests.Models;

/// <summary>Every provider that uses the OpenAI client sends chat through the pooled gannet-model-chat HttpClient, with the SDK's own network timeout off.</summary>
public class OpenAiClientTransportTests
{
    private const string Secret = "sk-transport-secret";

    private const string ChatReply = """
        { "id": "c1", "object": "chat.completion", "created": 1761120000, "model": "m",
          "choices": [ { "index": 0, "message": { "role": "assistant", "content": "hi" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 } }
        """;

    private sealed class StubClientFactory(StubHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => handler.Client();
    }

    private static ChatClientFactory Factory(Dictionary<string, ModelOptions> models, StubHttpHandler handler) =>
        new(Options.Create(new GannetOptions { Models = new(models, StringComparer.OrdinalIgnoreCase) }), new StubClientFactory(handler));

    private static StubHttpHandler Replying() => new(_ => StubHttpHandler.Json(ChatReply));

    [Fact]
    public void The_options_turn_the_sdk_network_timeout_off_and_use_the_pooled_client_for_every_endpoint_shape()
    {
        var factory = Factory([], Replying());

        foreach (var endpoint in new string?[] { null, "https://r.openai.azure.com/openai/v1/", "http://localhost:8080/v1" })
        {
            var options = factory.OpenAiChatOptions(endpoint, null);

            Assert.Equal(Timeout.InfiniteTimeSpan, options.NetworkTimeout);
            Assert.IsType<HttpClientPipelineTransport>(options.Transport);
            Assert.Equal(endpoint is null ? new Uri("https://api.openai.com/v1") : new Uri(endpoint), options.Endpoint ?? new Uri("https://api.openai.com/v1"));
        }
    }

    [Fact]
    public async Task Openai_chat_goes_through_the_stub_to_the_openai_default_endpoint_with_the_bearer_key()
    {
        var handler = Replying();
        var factory = Factory(new() { ["o"] = new ModelOptions { Provider = "OpenAI", Model = "gpt-5.4", ApiKey = Secret } }, handler);

        await (await factory.GetAsync("o", CancellationToken.None)).Client.GetResponseAsync("hi");

        var chat = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/chat/completions", chat.Uri.ToString());
        Assert.Equal($"Bearer {Secret}", chat.Headers["Authorization"]);
        Assert.DoesNotContain(Secret, chat.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_chat_goes_through_the_stub_to_the_configured_endpoint()
    {
        var handler = Replying();
        var factory = Factory(new() { ["z"] = new ModelOptions { Provider = "AzureOpenAI", Model = "dep", ApiKey = Secret, Endpoint = "https://r.openai.azure.com/openai/v1/" } }, handler);

        await (await factory.GetAsync("z", CancellationToken.None)).Client.GetResponseAsync("hi");

        var chat = Assert.Single(handler.Requests);
        Assert.Equal("https://r.openai.azure.com/openai/v1/chat/completions", chat.Uri.ToString());
        Assert.Equal($"Bearer {Secret}", chat.Headers["Authorization"]);
    }

    [Theory]
    [InlineData(Secret, $"Bearer {Secret}")]
    [InlineData(null, "Bearer not-needed")]
    [InlineData("  ", "Bearer not-needed")]
    public async Task Compatible_chat_goes_through_the_stub_to_the_configured_endpoint(string? key, string expectedAuthorization)
    {
        var handler = Replying();
        var factory = Factory(new() { ["c"] = new ModelOptions { Provider = "OpenAICompatible", Model = "m", ApiKey = key, Endpoint = "http://spark:8000/v1", ContextWindow = 4096 } }, handler);

        await (await factory.GetAsync("c", CancellationToken.None)).Client.GetResponseAsync("hi");

        var chat = Assert.Single(handler.Requests);
        Assert.Equal("http://spark:8000/v1/chat/completions", chat.Uri.ToString());
        Assert.Equal(expectedAuthorization, chat.Headers["Authorization"]);
    }

    [Theory]
    [InlineData("OpenAI", null)]
    [InlineData("AzureOpenAI", "https://r.openai.azure.com/openai/v1/")]
    [InlineData("OpenAICompatible", "http://spark:8000/v1")]
    [InlineData("OpenRouter", null)]
    public async Task Providers_other_than_deepseek_never_send_a_thinking_field(string provider, string? endpoint)
    {
        var handler = Replying();
        var factory = Factory(new() { ["m"] = new ModelOptions { Provider = provider, Model = "x", ApiKey = Secret, Endpoint = endpoint, ContextWindow = 4096, SupportsTools = true } }, handler);

        await (await factory.GetAsync("m", CancellationToken.None)).Client.GetResponseAsync("hi");

        Assert.False(JsonDocument.Parse(handler.Requests.Single(r => r.Method == HttpMethod.Post).Body!).RootElement.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task Deepseek_chat_always_asks_for_thinking_disabled_and_keeps_the_rest_of_the_body()
    {
        var handler = Replying();
        var factory = Factory(new() { ["d"] = new ModelOptions { Provider = "DeepSeek", Model = "deepseek-flash", ApiKey = Secret } }, handler);
        var tool = AIFunctionFactory.Create(() => "sunny", "weather", "The weather.");

        await (await factory.GetAsync("d", CancellationToken.None)).Client
            .GetResponseAsync("hi", new ChatOptions { Tools = [tool], Temperature = 0.3f });

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("deepseek-flash", body.GetProperty("model").GetString());
        Assert.Equal("weather", body.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal(0.3, body.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal("hi", body.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Deepseek_chat_overrides_a_thinking_value_the_caller_set()
    {
        var handler = Replying();
        var factory = Factory(new() { ["d"] = new ModelOptions { Provider = "DeepSeek", Model = "deepseek-flash", ApiKey = Secret } }, handler);
        var options = new ChatOptions { AdditionalProperties = new() { ["thinking"] = new { type = "enabled" } } };

        await (await factory.GetAsync("d", CancellationToken.None)).Client.GetResponseAsync("hi", options);

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
    }
}
