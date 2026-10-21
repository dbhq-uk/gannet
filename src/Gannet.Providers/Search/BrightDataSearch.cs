using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

/// <summary>Google results through Bright Data's SERP API (REST, never the bdata CLI). Opt-in only.</summary>
internal sealed class BrightDataSearch : SearchProviderBase
{
    // Bright Data's proxy error catalogue: client_10020 is "Account is suspended", which happens automatically when the balance goes negative. On the
    // Direct API the outer status can be 200, so the x-brd-err-code header is the signal.
    private const string SuspendedCode = "client_10020";

    private readonly string zone;

    public BrightDataSearch(HttpClient http, string name, ServiceOptions options) : base(http, name, options)
    {
        zone = string.IsNullOrWhiteSpace(options.Zone)
            ? throw new ArgumentException($"Bright Data needs a SERP zone: set Gannet:Search:{name}:Zone.", nameof(options))
            : options.Zone;
    }

    public override string Type => "brightdata";

    public override string DisplayName => "Bright Data SERP";

    public override ServiceTerms Terms => new(false,
        "Bright Data \"may retain data Client has collected and may use it for its own purposes in its sole discretion\": avoid it for confidential questions.");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var google = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}&brd_json=1&gl={Uri.EscapeDataString(Country())}&hl=en";
        var request = PostJson("https://api.brightdata.com/request", new { zone, url = google, format = "raw" });
        request.Headers.Authorization = new("Bearer", RequireKey());
        using var json = await SendAsync(request, ct);

        var root = json.RootElement;
        JsonDocument? inner = null;
        try
        {
            // The docs disagree on whether the parsed result arrives bare or wrapped (facts s.5): accept both.
            if (root.Prop("body").ValueKind == JsonValueKind.String)
            {
                inner = JsonDocument.Parse(root.Str("body"));
                root = inner.RootElement;
            }

            return Take(root.Prop("organic").Items().Select(Map), maxResults);
        }
        catch (JsonException)
        {
            // Never quote the body: it is page text from the service.
            throw new ServiceFailedException(DisplayName, "Bright Data returned a body that is not parsed results. Check that the zone is a SERP zone.");
        }
        finally
        {
            inner?.Dispose();
        }
    }

    protected override Task<bool> IsQuotaResponseAsync(HttpResponseMessage response, CancellationToken ct) =>
        Task.FromResult(response.Headers.TryGetValues("x-brd-err-code", out var codes) && codes.Contains(SuspendedCode, StringComparer.OrdinalIgnoreCase));

    // gl is a two-letter country code. Anything else, including anything that could add a parameter, falls back to gb.
    private string Country() =>
        Options.Country is { Length: 2 } country && country.All(char.IsAsciiLetter) ? country.ToLowerInvariant() : "gb";

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("link"));
        return url is null ? null : new SearchHit(r.Str("title"), url, r.Str("description"));
    }
}
