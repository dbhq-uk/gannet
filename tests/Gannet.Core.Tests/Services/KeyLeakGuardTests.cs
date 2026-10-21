using System.Reflection;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Core.Services;
using Microsoft.Extensions.AI;

namespace Gannet.Core.Tests.Services;

public class KeyLeakGuardTests
{
    private const string Secret = "sk-SECRET";

    private sealed class StubClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static GannetOptions OptionsWithSecrets()
    {
        var options = new GannetOptions();
        options.Models["local"] = new ModelOptions { Provider = "OpenAI", Model = "gpt", ApiKey = Secret };
        options.Search["tavily"] = new ServiceOptions { ApiKey = Secret };
        options.Readers.Services["jina"] = new ServiceOptions { ApiKey = Secret };
        options.Readers.Proxy = new ProxyOptions { Url = "http://proxy.example", Username = "user", Password = Secret };
        options.Storage.S3.SecretAccessKey = Secret;
        options.Storage.S3.AccessKeyId = Secret;
        return options;
    }

    private static object[] PublicValues(GannetOptions options)
    {
        var model = options.Models["local"];
        var search = options.Search["tavily"];
        var terms = new ServiceTerms(false, "note");
        var capabilities = new ModelCapabilities(32_000, true);
        return
        [
            terms,
            new SearchHit("title", new Uri("https://example.com/a"), "snippet"),
            new PageContent(new Uri("https://example.com/a"), "title", "text", "builtin"),
            new ServiceDescriptor("tavily", search.Type ?? "tavily", "Tavily", terms, true),
            new ModelDescriptor("local", model.Provider, model.Model),
            new ServiceCheckResult(true, "ok"),
            capabilities,
            new ModelHandle("local", new StubClient(), capabilities),
            new RateLimitedException("Tavily", TimeSpan.FromSeconds(1), "limited"),
            new ChallengeDetectedException("Reader", "challenge"),
            new AuthRequiredException("Tavily", "auth"),
            new QuotaExhaustedException("Tavily", "quota"),
            new ServiceFailedException("Tavily", "failed"),
            new UnknownServiceException("model", "nope", options.Models.Keys),
        ];
    }

    [Fact]
    public void No_public_value_prints_a_secret()
    {
        var options = OptionsWithSecrets();
        foreach (var value in PublicValues(options))
        {
            Assert.DoesNotContain(Secret, value.ToString(), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Secret, options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void No_public_descriptor_serialises_a_secret()
    {
        var options = OptionsWithSecrets();
        foreach (var value in PublicValues(options).Where(v => v is not ModelHandle and not Exception))
        {
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(value, value.GetType()), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Service_and_model_types_have_no_member_that_could_carry_a_key()
    {
        var types = typeof(GannetOptions).Assembly.GetExportedTypes()
            .Where(t => t.Namespace is "Gannet.Core.Services" or "Gannet.Core.Models");
        var risky = new[] { "key", "secret", "password", "credential" };
        foreach (var type in types)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(risky, word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
