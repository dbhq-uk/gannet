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
            await HttpErrors.EnsureSuccessAsync(response, DisplayName, website: false, ct);
            return await JsonRead.ParseAsync(response, DisplayName, ct);
        }
    }

    protected static IReadOnlyList<SearchHit> Take(IEnumerable<SearchHit?> hits, int maxResults) =>
        [.. hits.OfType<SearchHit>().Take(maxResults)];
}
