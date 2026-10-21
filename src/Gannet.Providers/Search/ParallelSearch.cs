using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

internal sealed class ParallelSearch(HttpClient http, string name, ServiceOptions options) : SearchProviderBase(http, name, options)
{
    // Parallel caps results at 20 and reduces a higher max_results to 20 with a warning (Advanced Search Settings, advanced_settings.max_results).
    private const int MaxResultsAllowed = 20;

    public override string Type => "parallel";

    public override string DisplayName => "Parallel";

    public override ServiceTerms Terms => new(false,
        "Results may go into a report for the person who asked, but never into a cache for later queries (Customer Terms s.2(b) and s.2(c)(xi)).");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var key = RequireKey();
        var request = PostJson("https://api.parallel.ai/v1/search", new
        {
            objective = query,
            search_queries = new[] { query },
            advanced_settings = new { max_results = Math.Clamp(maxResults, 1, MaxResultsAllowed) },
        });
        request.Headers.Add("x-api-key", key);
        using var json = await SendAsync(request, ct);
        return Take(json.RootElement.Prop("results").Items().Select(Map), maxResults);
    }

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("url"));
        var excerpts = string.Join(" ", r.Prop("excerpts").Items().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()));
        return url is null ? null : new SearchHit(r.Str("title"), url, excerpts, null, JsonRead.Date(r.Str("publish_date")));
    }
}
