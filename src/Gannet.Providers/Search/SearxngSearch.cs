using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

/// <summary>A self-hosted SearXNG instance. JSON output must be enabled in its settings.yml (search.formats).</summary>
internal sealed class SearxngSearch : SearchProviderBase
{
    private readonly string endpoint;

    public SearxngSearch(HttpClient http, string name, ServiceOptions options) : base(http, name, options)
    {
        endpoint = string.IsNullOrWhiteSpace(options.Endpoint)
            ? throw new ArgumentException($"SearXNG needs an address: set Gannet:Search:{name}:Endpoint.", nameof(options))
            : options.Endpoint.TrimEnd('/');
    }

    public override string Type => "searxng";

    public override string DisplayName => "SearXNG";

    public override ServiceTerms Terms => new(true, "Self-hosted. The terms of the engines it queries still apply.");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        var url = $"{endpoint}/search?q={Uri.EscapeDataString(query)}&format=json&categories=general";
        try
        {
            using var json = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), ct);
            return Take(json.RootElement.Prop("results").Items().Select(Map), maxResults);
        }
        catch (AuthRequiredException)
        {
            throw new ServiceFailedException(DisplayName, "SearXNG refused JSON output (403). Add json under search.formats in its settings.yml.");
        }
    }

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("url"));
        return url is null ? null : new SearchHit(r.Str("title"), url, r.Str("content"), null, JsonRead.Date(r.Str("publishedDate")));
    }
}
