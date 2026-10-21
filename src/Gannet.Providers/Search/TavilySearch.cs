using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

/// <summary>Tavily search. With no key it uses keyless mode, the zero-setup default (rate limited, best effort).</summary>
internal sealed class TavilySearch(HttpClient http, string name, ServiceOptions options) : SearchProviderBase(http, name, options)
{
    // Tavily accepts at most 20 results per call.
    private const int MaxResultsAllowed = 20;

    public override string Type => "tavily";

    public override string DisplayName => "Tavily";

    public override bool ReturnsPageContent => true;

    public override ServiceTerms Terms => new(false,
        "Keyless mode is rate limited and best effort; add a key or run SearXNG for more. Tavily may use inputs to its AI features to train models (terms s.6.5).");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var endpoint = (Options.Endpoint ?? "https://api.tavily.com").TrimEnd('/');
        var request = PostJson($"{endpoint}/search", new
        {
            query,
            max_results = Math.Clamp(maxResults, 1, MaxResultsAllowed),
            search_depth = "basic",
            include_raw_content = "markdown",

            // published_date is only returned when this is true (docs, results[].published_date).
            include_published_date = true,
        });

        // The key goes in the Authorization header only, never in the URL. With both set, Tavily would bill the key, so keyless drops it.
        if (string.IsNullOrWhiteSpace(Options.ApiKey) || Options.Keyless)
        {
            request.Headers.Add("X-Tavily-Access-Mode", "keyless");
        }
        else
        {
            request.Headers.Authorization = new("Bearer", Options.ApiKey);
        }

        using var json = await SendAsync(request, ct);
        return Take(json.RootElement.Prop("results").Items().Select(Map), maxResults);
    }

    // Documented by Tavily: 432 is the plan limit, 433 the pay-as-you-go limit.
    protected override bool IsQuotaStatus(HttpStatusCode status) => (int)status is 432 or 433;

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("url"));
        if (url is null)
        {
            return null;
        }

        var raw = r.Str("raw_content");
        return new SearchHit(r.Str("title"), url, r.Str("content"), raw.Length == 0 ? null : raw, JsonRead.Date(r.Str("published_date")));
    }
}
