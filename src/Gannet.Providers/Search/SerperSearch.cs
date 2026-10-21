using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

internal sealed class SerperSearch(HttpClient http, string name, ServiceOptions options) : SearchProviderBase(http, name, options)
{
    // Serper's public pages give no limit for num. Third-party guides give 20 as the cap and examples use 10 and 20, so 20 is the most gannet asks for.
    private const int MaxResultsAllowed = 20;

    public override string Type => "serper";

    public override string DisplayName => "Serper";

    public override ServiceTerms Terms => new(false, "Google results collected by scraping; Serper's terms give no legal shield.");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var request = PostJson("https://google.serper.dev/search", new { q = query, num = Math.Clamp(maxResults, 1, MaxResultsAllowed) });
        request.Headers.Add("X-API-KEY", RequireKey());
        using var json = await SendAsync(request, ct);
        return Take(json.RootElement.Prop("organic").Items().Select(Map), maxResults);
    }

    // Not in Serper's public pages: running out of credits is reported as a 400 whose message is "Not enough credits". Any other 400 is a plain failure.
    protected override async Task<bool> IsQuotaResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        using var body = await ReadErrorBodyAsync(response, ct);
        return body is not null && body.RootElement.Str("message").Contains("not enough credits", StringComparison.OrdinalIgnoreCase);
    }

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("link"));
        return url is null ? null : new SearchHit(r.Str("title"), url, r.Str("snippet"), null, JsonRead.Date(r.Str("date")));
    }
}
