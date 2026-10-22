using System.Net;
using Gannet.Providers.Models;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Models;

public class OpenAiCompatibleInfoTests
{
    private const string Vllm = """
        { "object": "list", "data": [
            { "id": "other-model", "object": "model", "max_model_len": 1111 },
            { "id": "qwen3-32b", "object": "model", "owned_by": "vllm", "root": "Qwen/Qwen3-32B", "max_model_len": 131072 } ] }
        """;

    private const string LlamaCppModels = """
        { "object": "list", "data": [ { "id": "../models/m.gguf", "object": "model", "owned_by": "llamacpp", "meta": { "n_ctx_train": 131072 } } ] }
        """;

    private const string LlamaCppProps = """
        { "default_generation_settings": { "id": 0, "n_ctx": 65536, "params": {} }, "total_slots": 1 }
        """;

    private static StubHttpHandler Server(Func<string, HttpResponseMessage?> route) =>
        new(request => route(request.RequestUri!.PathAndQuery) ?? StubHttpHandler.Json("{}", HttpStatusCode.NotFound));

    [Theory]
    [InlineData("http://localhost:8000/v1", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1/", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000", "http://localhost:8000/models")]
    [InlineData("http://localhost:8000/", "http://localhost:8000/models")]
    [InlineData("http://host/llm/v1/", "http://host/llm/v1/models")]
    public async Task A_vllm_answer_gives_max_model_len_whatever_the_endpoint_shape(string endpoint, string expectedFirstUrl)
    {
        var handler = Server(path => path.EndsWith("/models", StringComparison.Ordinal) ? StubHttpHandler.Json(Vllm) : null);

        var context = await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri(endpoint), "qwen3-32b", CancellationToken.None);

        Assert.Equal(131072, context);
        Assert.Equal(expectedFirstUrl, handler.Requests[0].Uri.ToString());
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Theory]
    [InlineData("http://localhost:8080/v1", "http://localhost:8080/props")]
    [InlineData("http://localhost:8080/v1/", "http://localhost:8080/props")]
    [InlineData("http://localhost:8080", "http://localhost:8080/props")]
    [InlineData("http://localhost:8080/", "http://localhost:8080/props")]
    [InlineData("http://host/llm/v1/", "http://host/llm/props")]
    public async Task A_llama_cpp_answer_gives_n_ctx_from_props_at_the_server_root(string endpoint, string propsUrl)
    {
        var handler = Server(path => path switch
        {
            var p when p.EndsWith("/models", StringComparison.Ordinal) => StubHttpHandler.Json(LlamaCppModels),
            var p when p.EndsWith("/props", StringComparison.Ordinal) => StubHttpHandler.Json(LlamaCppProps),
            _ => null,
        });

        var context = await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri(endpoint), "../models/m.gguf", CancellationToken.None);

        Assert.Equal(65536, context);
        Assert.Contains(propsUrl, handler.Requests.Select(r => r.Uri.ToString()));
    }

    [Fact]
    public async Task A_404_on_everything_gives_null()
    {
        var handler = Server(_ => null);
        Assert.Null(await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri("http://localhost:8080/v1"), "m", CancellationToken.None));
        Assert.NotEmpty(handler.Requests);
    }

    [Fact]
    public async Task A_model_the_server_does_not_list_gives_null_from_models()
    {
        var handler = Server(path => path.EndsWith("/models", StringComparison.Ordinal) ? StubHttpHandler.Json(Vllm) : null);
        Assert.Null(await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri("http://localhost:8000/v1"), "not-served", CancellationToken.None));
    }

    [Theory]
    [InlineData("""{ "data": [ { "id": "m", "max_model_len": 0 } ] }""")]
    [InlineData("""{ "data": [ { "id": "m", "max_model_len": "big" } ] }""")]
    [InlineData("""{ "data": [ { "id": "m", "max_model_len": 99999999999 } ] }""")]
    [InlineData("""{ "data": [ { "id": "m" } ] }""")]
    [InlineData("""not json""")]
    [InlineData("""[]""")]
    public async Task A_useless_answer_gives_null(string body)
    {
        var handler = Server(_ => StubHttpHandler.Json(body));
        Assert.Null(await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri("http://localhost:8000/v1"), "m", CancellationToken.None));
    }

    [Fact]
    public async Task An_unusable_n_ctx_gives_null()
    {
        var handler = Server(path => path.EndsWith("/props", StringComparison.Ordinal) ? StubHttpHandler.Json("""{ "default_generation_settings": { "n_ctx": 0 } }""") : null);
        Assert.Null(await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri("http://localhost:8080/v1"), "m", CancellationToken.None));
    }

    [Fact]
    public async Task A_server_that_refuses_gives_null_and_the_caller_cancelling_still_cancels()
    {
        var refusing = Server(_ => StubHttpHandler.Json("""{ "error": "nope" }""", HttpStatusCode.Unauthorized));
        Assert.Null(await OpenAiCompatibleInfo.ReadContextAsync(refusing.Client(), new Uri("http://localhost:8000/v1"), "m", CancellationToken.None));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenAiCompatibleInfo.ReadContextAsync(Server(_ => StubHttpHandler.Json(Vllm)).Client(), new Uri("http://localhost:8000/v1"), "qwen3-32b", cancelled.Token));
    }

    private const string ProbeKey = "sk-probe-secret-value";

    [Fact]
    public async Task A_configured_key_is_sent_as_a_bearer_header_on_both_probes_and_never_in_a_url()
    {
        var handler = Server(_ => null);

        await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri("http://localhost:8000/v1"), "m", CancellationToken.None, ProbeKey);

        Assert.Contains(handler.Requests, r => r.Uri.AbsolutePath.EndsWith("/models", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, r => r.Uri.AbsolutePath == "/props");
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal($"Bearer {ProbeKey}", r.Headers["Authorization"]);
            Assert.DoesNotContain(ProbeKey, r.Uri.ToString(), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_key_means_no_authorization_header(string? key)
    {
        var handler = Server(_ => null);

        await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri("http://localhost:8000/v1"), "m", CancellationToken.None, key);

        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, r => Assert.False(r.Headers.ContainsKey("Authorization")));
    }

    [Fact]
    public async Task A_401_on_both_probes_still_gives_null()
    {
        var handler = Server(_ => StubHttpHandler.Json($$"""{ "error": "bad key {{ProbeKey}}" }""", HttpStatusCode.Unauthorized));

        Assert.Null(await OpenAiCompatibleInfo.ReadContextAsync(handler.Client(), new Uri("http://localhost:8000/v1"), "m", CancellationToken.None, ProbeKey));
    }
}
