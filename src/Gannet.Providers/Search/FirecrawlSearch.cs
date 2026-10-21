using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

internal sealed class FirecrawlSearch(HttpClient http, string name, ServiceOptions options) : SearchProviderBase(http, name, options)
{
    // Firecrawl v2 accepts limit 1 to 100 and a query of at most 500 characters (API reference, POST /v2/search).
    private const int MaxResultsAllowed = 100;
    private const int MaxQueryLength = 500;

    public override string Type => "firecrawl";

    public override string DisplayName => "Firecrawl";

    public override ServiceTerms Terms => new(false, "1,000 free credits a month; keyless use has an unpublished daily cap per IP address.");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var endpoint = (Options.Endpoint ?? "https://api.firecrawl.dev").TrimEnd('/');
        var request = PostJson($"{endpoint}/v2/search", new { query = Clip(query), limit = Math.Clamp(maxResults, 1, MaxResultsAllowed) });

        // The key goes in the Authorization header only. Keyless mode sends no header at all, so a configured key is dropped when Keyless is set.
        if (!string.IsNullOrWhiteSpace(Options.ApiKey) && !Options.Keyless)
        {
            request.Headers.Authorization = new("Bearer", Options.ApiKey);
        }

        using var json = await SendAsync(request, ct);
        return Take(json.RootElement.Prop("data").Prop("web").Items().Select(Map), maxResults);
    }

    // Cut at the documented length without leaving half of a surrogate pair at the end.
    private static string Clip(string query)
    {
        if (query.Length <= MaxQueryLength)
        {
            return query;
        }

        var end = char.IsHighSurrogate(query[MaxQueryLength - 1]) ? MaxQueryLength - 1 : MaxQueryLength;
        return query[..end];
    }

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("url"));
        return url is null ? null : new SearchHit(r.Str("title"), url, r.Str("description"));
    }
}
