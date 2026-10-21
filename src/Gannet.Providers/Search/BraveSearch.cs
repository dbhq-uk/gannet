using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

internal sealed partial class BraveSearch(HttpClient http, string name, ServiceOptions options) : SearchProviderBase(http, name, options)
{
    // Brave accepts 1 to 20 results per call (query parameter count, default 20).
    private const int MaxResultsAllowed = 20;

    // The error.code values Brave documents on a 429 that mean a plan or credit limit, not a request rate.
    private static readonly string[] QuotaCodes = ["QUOTA_LIMITED", "USAGE_LIMIT_EXCEEDED", "CREDIT_EXHAUSTED"];

    public override string Type => "brave";

    public override string DisplayName => "Brave Search";

    public override ServiceTerms Terms => new(false,
        "Transient storage only, and results may not be used to train or evaluate AI models (Search API Terms, 1 Sep 2026).");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var key = RequireKey();
        var count = Math.Clamp(maxResults, 1, MaxResultsAllowed);
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={count}");
        request.Headers.Add("X-Subscription-Token", key);
        request.Headers.Accept.ParseAdd("application/json");
        using var json = await SendAsync(request, ct);
        return Take(json.RootElement.Prop("web").Prop("results").Items().Select(Map), maxResults);
    }

    // A 429 is either a request rate or a plan limit; only the error code in the body tells them apart.
    protected override async Task<bool> IsQuotaResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return false;
        }

        using var body = await ReadErrorBodyAsync(response, ct);
        return body is not null && QuotaCodes.Contains(body.RootElement.Prop("error").Str("code"), StringComparer.Ordinal);
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("url"));
        var snippet = WebUtility.HtmlDecode(Tags().Replace(r.Str("description"), string.Empty));
        return url is null ? null : new SearchHit(WebUtility.HtmlDecode(r.Str("title")), url, snippet, null, JsonRead.Date(r.Str("page_age")));
    }
}
