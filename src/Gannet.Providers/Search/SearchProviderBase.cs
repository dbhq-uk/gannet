using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

internal abstract class SearchProviderBase(HttpClient http, string name, ServiceOptions options) : ISearchProvider
{
    private const int MaxErrorBodyBytes = 64 * 1024;

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

    /// <summary>
    /// Called before the standard status mapping. The default is <see cref="IsQuotaStatus"/>. Override it for a service that signals a plan or credit
    /// limit in the body of a status it also uses for something else. Read the body with <see cref="ReadErrorBodyAsync"/> and never copy it into a message.
    /// </summary>
    protected virtual Task<bool> IsQuotaResponseAsync(HttpResponseMessage response, CancellationToken ct) =>
        Task.FromResult(IsQuotaStatus(response.StatusCode));

    /// <summary>
    /// Reads a small JSON error body and returns what the predicate says about it. Anything wrong with the body (missing, too large, not JSON, bad UTF-8
    /// in a field the predicate reads) gives false, so the standard status mapping applies and nothing escapes.
    /// </summary>
    protected async Task<bool> ReadErrorBodyAsync(HttpResponseMessage response, Func<JsonElement, bool> check, CancellationToken ct)
    {
        try
        {
            using var body = await JsonRead.ParseAsync(response, DisplayName, MaxErrorBodyBytes, ct);
            return check(body.RootElement);
        }
        catch (Exception e) when (e is ServiceFailedException or InvalidOperationException or JsonException or HttpRequestException or IOException)
        {
            return false;
        }
    }

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
            // Headers first, so the body cap in JsonRead applies before anything is buffered.
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (await IsQuotaResponseAsync(response, ct))
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
