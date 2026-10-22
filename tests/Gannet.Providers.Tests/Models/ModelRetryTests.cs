using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Models;
using Gannet.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Gannet.Providers.Tests.Models;

// The model SDKs retry on their own. They are bounded to two retries (three requests), and a long Retry-After from the service is capped, because both the
// OpenAI-format and the Anthropic SDK wait for as long as the header says (an hour, in a probe) and ignore their own limits.
[Collection("Gemini environment")]
public class ModelRetryTests
{
    private const string ErrorBody = """{ "type": "error", "error": { "type": "api_error", "message": "overloaded" } }""";

    private sealed class StubClientFactory(StubHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => handler.Client();
    }

    private static ModelOptions Model(string provider) => provider switch
    {
        "OpenAI" => new ModelOptions { Provider = provider, Model = "gpt-5.4", ApiKey = "k" },
        "AzureOpenAI" => new ModelOptions { Provider = provider, Model = "dep", ApiKey = "k", Endpoint = "https://r.openai.azure.com/openai/v1/" },
        "OpenAICompatible" => new ModelOptions { Provider = provider, Model = "m", Endpoint = "http://spark:8000/v1", ContextWindow = 4096 },
        "OpenRouter" => new ModelOptions { Provider = provider, Model = "acme/big", ApiKey = "k", ContextWindow = 4096, SupportsTools = true },
        "DeepSeek" => new ModelOptions { Provider = provider, Model = "deepseek-flash", ApiKey = "k" },
        "Anthropic" => new ModelOptions { Provider = provider, Model = "claude-sonnet-5-5", ApiKey = "k" },
        _ => new ModelOptions { Provider = provider, Model = "gemini-3.6-flash", ApiKey = "k" },
    };

    private static IChatClient Client(string provider, StubHttpHandler handler)
    {
        var factory = new ChatClientFactory(
            Options.Create(new GannetOptions { Models = new(StringComparer.OrdinalIgnoreCase) { ["m"] = Model(provider) } }),
            new StubClientFactory(handler));
        return factory.GetAsync("m", CancellationToken.None).GetAwaiter().GetResult().Client;
    }

    private static HttpResponseMessage Unavailable(string? retryAfter)
    {
        var response = StubHttpHandler.Json(ErrorBody, HttpStatusCode.ServiceUnavailable);
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        return response;
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("AzureOpenAI")]
    [InlineData("OpenAICompatible")]
    [InlineData("OpenRouter")]
    [InlineData("DeepSeek")]
    [InlineData("Anthropic")]
    [InlineData("Gemini")]
    public async Task A_service_that_always_answers_503_is_asked_at_most_three_times(string provider)
    {
        var handler = new StubHttpHandler(_ => Unavailable(null));
        var client = Client(provider, handler);

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => client.GetResponseAsync("hi"));

        Assert.Equal($"{provider} answered 503.", error.Message);
        Assert.InRange(handler.Requests.Count, 1, 3);
        if (provider != "Gemini")
        {
            Assert.Equal(3, handler.Requests.Count);
        }
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task A_long_retry_after_is_capped_at_five_seconds(string provider)
    {
        var handler = new StubHttpHandler(_ => Unavailable("3600"));
        var client = Client(provider, handler);

        // Two waits of at most 5 s. Uncapped, the first wait alone would be an hour, so the 2-minute limit fails the test instead of hanging it.
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => client.GetResponseAsync("hi", cancellationToken: limit.Token));

        Assert.Equal($"{provider} answered 503.", error.Message);
        Assert.Equal(3, handler.Requests.Count);
    }
}
