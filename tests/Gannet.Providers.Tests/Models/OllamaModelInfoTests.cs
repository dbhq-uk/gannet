using System.Text.Json;
using Gannet.Core.Services;
using Gannet.Providers.Models;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Models;

public class OllamaModelInfoTests
{
    [Fact]
    public async Task Reads_context_length_for_the_architecture_and_tool_capability()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "capabilities": ["completion", "tools"],
              "model_info": { "general.architecture": "qwen3", "qwen3.context_length": 40960, "llama.context_length": 1 } }
            """));

        var info = await OllamaModelInfo.ReadAsync(handler.Client(), new Uri("http://localhost:11434"), "qwen3:4b", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:11434/api/show", request.Uri.ToString());
        Assert.Equal("qwen3:4b", JsonDocument.Parse(request.Body!).RootElement.GetProperty("model").GetString());
        Assert.Equal((40960, true), info);
    }

    [Fact]
    public async Task Missing_fields_mean_small_context_and_no_tools()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "model_info": {} }"""));
        Assert.Equal((4096, false), await OllamaModelInfo.ReadAsync(handler.Client(), new Uri("http://localhost:11434"), "m", CancellationToken.None));
    }

    [Theory]
    [InlineData("1.5e12")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("\"big\"")]
    public async Task A_context_length_that_is_not_a_usable_integer_means_small_context(string length)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($$"""{ "model_info": { "general.architecture": "a", "a.context_length": {{length}} } }"""));
        var (context, _) = await OllamaModelInfo.ReadAsync(handler.Client(), new Uri("http://localhost:11434"), "m", CancellationToken.None);
        Assert.Equal(4096, context);
    }

    [Fact]
    public async Task A_path_prefix_on_the_endpoint_is_kept()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{}"));
        await OllamaModelInfo.ReadAsync(handler.Client(), new Uri("http://proxy.local/ollama/"), "m", CancellationToken.None);
        Assert.Equal("http://proxy.local/ollama/api/show", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task A_refusal_is_a_typed_error_that_does_not_echo_the_body()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "error": "leaky-body-text" }""", System.Net.HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => OllamaModelInfo.ReadAsync(handler.Client(), new Uri("http://localhost:11434"), "m", CancellationToken.None));
        Assert.DoesNotContain("leaky-body-text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_model_is_a_typed_error()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "error": "model 'x' not found" }""", System.Net.HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => OllamaModelInfo.ReadAsync(handler.Client(), new Uri("http://localhost:11434"), "x", CancellationToken.None));
        Assert.DoesNotContain("not found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_never_answers_is_a_typed_timeout_but_the_callers_cancellation_still_cancels()
    {
        var hang = new HangingHandler();
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() =>
            OllamaModelInfo.ReadAsync(new HttpClient(hang), new Uri("http://localhost:11434"), "m", CancellationToken.None, TimeSpan.FromMilliseconds(50)));
        Assert.Contains("timed out", error.Message, StringComparison.Ordinal);

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OllamaModelInfo.ReadAsync(new HttpClient(hang), new Uri("http://localhost:11434"), "m", cancelled.Token, TimeSpan.FromSeconds(30)));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage();
        }
    }
}
