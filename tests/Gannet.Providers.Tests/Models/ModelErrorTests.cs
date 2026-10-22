using System.ClientModel.Primitives;
using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Core.Services;
using Gannet.Providers.Models;
using Gannet.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Gannet.Providers.Tests.Models;

public class ModelErrorTests
{
    private const string Secret = "sk-ant-planted-SECRET-fragment";

    // One body that every SDK can parse: Anthropic and OpenAI error shapes both nest the message under "error".
    private static readonly string ErrorBody = $$"""{ "type": "error", "error": { "type": "authentication_error", "code": "invalid_api_key", "message": "Incorrect API key provided: {{Secret}}" } }""";

    // Ollama's own error body is {"error": "text"}.
    private static readonly string OllamaError = $$"""{ "error": "Incorrect API key provided: {{Secret}}" }""";

    private const string OllamaShow = """{ "capabilities": ["tools"], "model_info": { "general.architecture": "qwen3", "qwen3.context_length": 8192 } }""";

    private sealed class StubClientFactory(StubHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => handler.Client();
    }

    private static async Task<IChatClient> ClientAsync(string provider, HttpStatusCode status)
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/api/show", StringComparison.Ordinal)
            ? StubHttpHandler.Json(OllamaShow)
            : StubHttpHandler.Json(provider == "Ollama" ? OllamaError : ErrorBody, status));
        var factory = new ChatClientFactory(
            Options.Create(new GannetOptions
            {
                Models = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["m1"] = new ModelOptions { Provider = provider, Model = provider == "Anthropic" ? "claude-sonnet-5-5" : "some-model", ApiKey = Secret },
                },
            }),
            new StubClientFactory(handler));
        return (await factory.GetAsync("m1", CancellationToken.None)).Client;
    }

    private static async Task<Exception> FailAsync(IChatClient client, bool stream)
    {
        try
        {
            if (stream)
            {
                await foreach (var _ in client.GetStreamingResponseAsync("hi"))
                {
                }
            }
            else
            {
                await client.GetResponseAsync("hi");
            }
        }
        catch (Exception e)
        {
            return e;
        }

        throw new Xunit.Sdk.XunitException("The call did not fail.");
    }

    private static void AssertClean(ServiceException error)
    {
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("authentication_error", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("OpenAI", false)]
    [InlineData("OpenAI", true)]
    [InlineData("Anthropic", false)]
    [InlineData("Anthropic", true)]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    [InlineData("Ollama", false)]
    [InlineData("Ollama", true)]
    public async Task A_401_from_each_sdk_is_auth_required_naming_the_config_key_and_never_the_body(string provider, bool stream)
    {
        var client = await ClientAsync(provider, HttpStatusCode.Unauthorized);

        var error = Assert.IsType<AuthRequiredException>(await FailAsync(client, stream));

        Assert.Contains("Gannet:Models:m1:ApiKey", error.Message, StringComparison.Ordinal);
        Assert.Equal(provider, error.Service);
        AssertClean(error);
    }

    [Theory]
    [InlineData("OpenAI", false)]
    [InlineData("Anthropic", false)]
    [InlineData("Anthropic", true)]
    [InlineData("Gemini", false)]
    [InlineData("Ollama", true)]
    public async Task A_403_is_auth_required(string provider, bool stream)
    {
        var client = await ClientAsync(provider, HttpStatusCode.Forbidden);

        AssertClean(Assert.IsType<AuthRequiredException>(await FailAsync(client, stream)));
    }

    [Theory]
    [InlineData("OpenAI", false)]
    [InlineData("OpenAI", true)]
    [InlineData("Anthropic", false)]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    [InlineData("Ollama", false)]
    public async Task A_402_is_quota_exhausted(string provider, bool stream)
    {
        var client = await ClientAsync(provider, HttpStatusCode.PaymentRequired);

        var error = Assert.IsType<QuotaExhaustedException>(await FailAsync(client, stream));

        Assert.Contains("402", error.Message, StringComparison.Ordinal);
        AssertClean(error);
    }

    [Theory]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    [InlineData("Ollama", false)]
    [InlineData("Ollama", true)]
    public async Task A_429_is_rate_limited(string provider, bool stream)
    {
        var client = await ClientAsync(provider, HttpStatusCode.TooManyRequests);

        var error = Assert.IsType<RateLimitedException>(await FailAsync(client, stream));

        Assert.Null(error.RetryAfter);
        AssertClean(error);
    }

    [Theory]
    [InlineData("OpenAI", false)]
    [InlineData("OpenAI", true)]
    [InlineData("Anthropic", false)]
    [InlineData("Anthropic", true)]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    public async Task Any_other_status_is_a_failure_that_names_the_status_only(string provider, bool stream)
    {
        var client = await ClientAsync(provider, HttpStatusCode.BadRequest);

        var error = Assert.IsType<ServiceFailedException>(await FailAsync(client, stream));

        Assert.Equal($"{provider} answered 400.", error.Message);
        AssertClean(error);
    }

    // OllamaSharp turns an error body of its own shape into its own exception, which carries no status.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_ollama_error_body_is_a_failure_with_fixed_wording(bool stream)
    {
        var client = await ClientAsync("Ollama", HttpStatusCode.BadRequest);

        var error = Assert.IsType<ServiceFailedException>(await FailAsync(client, stream));

        Assert.Equal("Ollama returned an error.", error.Message);
        AssertClean(error);
    }

    private sealed class ThrowingClient(Func<Exception> failure, bool midStream = false) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(failure());

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (midStream)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            }

            await Task.Yield();
            throw failure();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeResponse(int status, string? retryAfter) : PipelineResponse
    {
        private readonly HeadersImpl headers = new(retryAfter);

        public override int Status => status;

        public override string ReasonPhrase => "reason";

        public override Stream? ContentStream { get => null; set { } }

        public override BinaryData Content => BinaryData.FromString(ErrorBody);

        protected override PipelineResponseHeaders HeadersCore => headers;

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Content);

        public override void Dispose()
        {
        }

        private sealed class HeadersImpl(string? retryAfter) : PipelineResponseHeaders
        {
            public override bool TryGetValue(string name, out string? value)
            {
                value = string.Equals(name, "Retry-After", StringComparison.OrdinalIgnoreCase) ? retryAfter : null;
                return value is not null;
            }

            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                var found = TryGetValue(name, out var one);
                values = found ? [one!] : null;
                return found;
            }

            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                (retryAfter is null ? Enumerable.Empty<KeyValuePair<string, string>>() : [new("Retry-After", retryAfter)]).GetEnumerator();
        }
    }

    private static async Task<System.ClientModel.ClientResultException> OpenAiFailureAsync(int status, string? retryAfter) =>
        await System.ClientModel.ClientResultException.CreateAsync(new FakeResponse(status, retryAfter));

    [Fact]
    public async Task An_openai_429_carries_the_retry_after_header()
    {
        var failure = await OpenAiFailureAsync(429, "17");
        var client = new TypedErrorChatClient(new ThrowingClient(() => failure), "OpenAI", "m1");

        var error = Assert.IsType<RateLimitedException>(await FailAsync(client, stream: false));

        Assert.Equal(TimeSpan.FromSeconds(17), error.RetryAfter);
        AssertClean(error);
    }

    [Fact]
    public async Task An_openai_429_without_a_usable_retry_after_has_none()
    {
        var failure = await OpenAiFailureAsync(429, "soon");
        var client = new TypedErrorChatClient(new ThrowingClient(() => failure), "OpenAI", "m1");

        Assert.Null(Assert.IsType<RateLimitedException>(await FailAsync(client, stream: false)).RetryAfter);
    }

    [Fact]
    public async Task A_mid_stream_failure_is_mapped_after_the_first_update_was_delivered()
    {
        var failure = await OpenAiFailureAsync(401, null);
        var client = new TypedErrorChatClient(new ThrowingClient(() => failure, midStream: true), "OpenAI", "m1");
        var seen = new List<string>();

        var error = await Assert.ThrowsAsync<AuthRequiredException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("hi"))
            {
                seen.Add(update.Text);
            }
        });

        Assert.Equal(["partial"], seen);
        AssertClean(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_connection_failure_is_could_not_be_reached(bool stream)
    {
        var client = new TypedErrorChatClient(new ThrowingClient(() => new HttpRequestException($"connect failed for {Secret}")), "Ollama", "m1");

        var error = Assert.IsType<ServiceFailedException>(await FailAsync(client, stream));

        Assert.Equal("Ollama could not be reached.", error.Message);
        AssertClean(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_timeout_that_is_not_the_callers_is_a_service_failure(bool stream)
    {
        var client = new TypedErrorChatClient(new ThrowingClient(() => new TaskCanceledException(Secret)), "Gemini", "m1");

        var error = Assert.IsType<ServiceFailedException>(await FailAsync(client, stream));

        Assert.Equal("Gemini timed out.", error.Message);
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_escapes_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var client = new TypedErrorChatClient(new ThrowingClient(() => new OperationCanceledException(cts.Token)), "Gemini", "m1");
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync("hi", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task An_exception_gannet_does_not_know_passes_through()
    {
        var client = new TypedErrorChatClient(new ThrowingClient(() => new InvalidOperationException("a bug")), "OpenAI", "m1");

        Assert.IsType<InvalidOperationException>(await FailAsync(client, stream: false));
    }

    [Fact]
    public async Task An_ollama_error_is_a_failure_with_fixed_wording()
    {
        var client = new TypedErrorChatClient(new ThrowingClient(() => new OllamaSharp.Models.Exceptions.OllamaException($"model said {Secret}")), "Ollama", "m1");

        var error = Assert.IsType<ServiceFailedException>(await FailAsync(client, stream: false));

        Assert.Equal("Ollama returned an error.", error.Message);
    }

    [Fact]
    public async Task Every_handle_the_factory_returns_is_wrapped()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{}"));
        var factory = new ChatClientFactory(
            Options.Create(new GannetOptions
            {
                Models = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["a"] = new ModelOptions { Provider = "OpenAI", Model = "gpt-5.4", ApiKey = "k" },
                    ["b"] = new ModelOptions { Provider = "OpenAICompatible", Endpoint = "http://localhost:8080/v1", Model = "m", ContextWindow = 4096 },
                    ["c"] = new ModelOptions { Provider = "AzureOpenAI", Endpoint = "https://r.openai.azure.com/openai/v1/", Model = "d", ApiKey = "k" },
                    ["d"] = new ModelOptions { Provider = "Anthropic", Model = "claude-opus-5-5", ApiKey = "k" },
                    ["e"] = new ModelOptions { Provider = "Gemini", Model = "gemini-3.6-flash", ApiKey = "k" },
                    ["f"] = new ModelOptions { Provider = "DeepSeek", Model = "deepseek-flash", ApiKey = "k" },
                    ["g"] = new ModelOptions { Provider = "OpenRouter", Model = "x/y", ApiKey = "k", ContextWindow = 4096, SupportsTools = true },
                },
            }),
            new StubClientFactory(handler));

        foreach (var name in new[] { "a", "b", "c", "d", "e", "f", "g" })
        {
            Assert.NotNull((await factory.GetAsync(name, CancellationToken.None)).Client.GetService<TypedErrorChatClient>());
        }
    }

    [Fact]
    public async Task The_wrapper_message_for_a_plain_http_error_never_echoes_a_reason_phrase()
    {
        var client = new TypedErrorChatClient(new ThrowingClient(() => new HttpRequestException(Secret, null, HttpStatusCode.BadGateway)), "Ollama", "m1");

        var error = Assert.IsType<ServiceFailedException>(await FailAsync(client, stream: false));

        Assert.Equal("Ollama answered 502.", error.Message);
    }
}
