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

    /// <summary>How long a response body may take to arrive. Tests shorten it.</summary>
    internal TimeSpan BodyDeadline { get; init; } = JsonRead.DefaultBodyDeadline;

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
    protected Task<bool> ReadErrorBodyAsync(HttpResponseMessage response, Func<JsonElement, bool> check, CancellationToken ct) =>
        ServiceHttp.ReadErrorBodyAsync(response, DisplayName, check, ct);

    /// <summary>
    /// Called with a response that has passed the status checks, before its body is read. Override it to refuse a response that carries a failure in its headers.
    /// Throw a typed error with gannet's own words; never copy a header into a message.
    /// </summary>
    protected virtual void InspectResponse(HttpResponseMessage response)
    {
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
            using var response = await ServiceHttp.SendCheckedAsync(Http, request, DisplayName, IsQuotaResponseAsync, ct);
            InspectResponse(response);
            return await JsonRead.ParseAsync(response, DisplayName, ct, BodyDeadline);
        }
    }

    private const int MaxTitleCharacters = 300;
    private const int MaxSnippetCharacters = 2000;

    /// <summary>
    /// Drops hits with a URL already seen (the first one stays), takes at most maxResults, and bounds what a service can put in a hit: a title is one line of at most
    /// 300 characters, a snippet at most 2,000. They reach the model's prompt, so their size and shape are the service's to choose only within these limits.
    /// </summary>
    protected static IReadOnlyList<SearchHit> Take(IEnumerable<SearchHit?> hits, int maxResults)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. hits.OfType<SearchHit>().Where(h => seen.Add(h.Url.AbsoluteUri)).Take(maxResults).Select(Bounded)];
    }

    private static SearchHit Bounded(SearchHit hit) => hit with
    {
        // Any run of white space, new lines and tabs included, becomes one space.
        Title = TextCap.Cut(string.Join(' ', hit.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), MaxTitleCharacters),
        Snippet = TextCap.Cut(hit.Snippet, MaxSnippetCharacters),
    };
}
