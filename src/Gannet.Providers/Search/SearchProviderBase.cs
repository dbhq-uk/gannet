using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

internal abstract class SearchProviderBase(HttpClient http, string name, ServiceOptions options) : ISearchProvider
{
    protected HttpClient Http { get; } = http;

    protected ServiceOptions Options { get; } = options;

    public string Name { get; } = name;

    public abstract string Type { get; }

    public abstract string DisplayName { get; }

    public abstract ServiceTerms Terms { get; }

    public virtual bool ReturnsPageContent => false;

    public abstract Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct);

    /// <summary>True for a status this service uses for a plan or credit limit, which is a quota error and not a plain failure.</summary>
    protected virtual bool IsQuotaStatus(HttpStatusCode status) => false;

    protected string RequireKey() =>
        string.IsNullOrWhiteSpace(Options.ApiKey)
            ? throw new AuthRequiredException(DisplayName, $"{DisplayName} needs an API key: set Gannet:Search:{Name}:ApiKey.")
            : Options.ApiKey;

    protected static HttpRequestMessage PostJson(string url, object body) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(body) };

    protected async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            using var response = await Http.SendAsync(request, ct);
            if (IsQuotaStatus(response.StatusCode))
            {
                throw new QuotaExhaustedException(DisplayName, $"{DisplayName} says the plan or credit limit is reached ({(int)response.StatusCode}).");
            }

            await HttpErrors.EnsureSuccessAsync(response, DisplayName, website: false, ct);
            return await JsonRead.ParseAsync(response, DisplayName, ct);
        }
    }

    /// <summary>Drops hits with a URL already seen (the first one stays), then takes at most maxResults.</summary>
    protected static IReadOnlyList<SearchHit> Take(IEnumerable<SearchHit?> hits, int maxResults)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. hits.OfType<SearchHit>().Where(h => seen.Add(h.Url.AbsoluteUri)).Take(maxResults)];
    }
}
