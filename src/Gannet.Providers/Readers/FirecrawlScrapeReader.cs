using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Readers;

internal sealed class FirecrawlScrapeReader(HttpClient http, string name, ServiceOptions options, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    : RemoteReaderBase(http, name, options, resolve)
{
    public override string Type => "firecrawl";

    public override string DisplayName => "Firecrawl scrape";

    public override ServiceTerms Terms => new(false, "Renders JavaScript pages. One credit per page; keyless use has a per-address daily cap.");

    private bool IsKeyless => string.IsNullOrWhiteSpace(Options.ApiKey) || Options.Keyless;

    // Keyless use is capped per IP address per day and the docs say exceeding the cap returns a 429, so with no key a 429 is a quota error. With a key a 429
    // is the per-minute plan rate or concurrency limit, which stays a rate limit. A 402 (credits used up) is mapped to a quota error by the standard mapping.
    protected override bool IsQuotaStatus(HttpStatusCode status) => IsKeyless && status == HttpStatusCode.TooManyRequests;

    protected override async Task<(string Title, string Markdown)> FetchAsync(Uri url, CancellationToken ct)
    {
        var endpoint = (Options.Endpoint ?? "https://api.firecrawl.dev").TrimEnd('/');
        var request = PostJson($"{endpoint}/v2/scrape", new { url = url.ToString(), formats = new[] { "markdown" }, onlyMainContent = true });

        // The key goes in the Authorization header only. Keyless mode sends no header at all, so a configured key is dropped when Keyless is set.
        if (!IsKeyless)
        {
            request.Headers.Authorization = new("Bearer", Options.ApiKey!);
        }

        using var json = await SendJsonAsync(request, ct);
        var root = json.RootElement;
        if (root.Prop("success").ValueKind == JsonValueKind.False)
        {
            throw new ServiceFailedException(DisplayName, $"{DisplayName} could not scrape the page.");
        }

        var data = root.Prop("data");
        var metadata = data.Prop("metadata");

        // metadata.statusCode is the page's own status. A page that answered with an error is not an article, whatever text it carries.
        if (metadata.Prop("statusCode") is { ValueKind: JsonValueKind.Number } status && status.TryGetInt32(out var code) && code >= 400)
        {
            throw new ServiceFailedException(DisplayName, $"The page answered {code} to {DisplayName}.");
        }

        return (TitleOf(metadata.Prop("title")), data.Str("markdown"));
    }

    // metadata.title can be a string or a list of strings (API reference, ScrapeResponse).
    private static string TitleOf(JsonElement title) =>
        title.ValueKind == JsonValueKind.Array
            ? title.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString() ?? string.Empty).FirstOrDefault(t => t.Length > 0) ?? string.Empty
            : title.ValueKind == JsonValueKind.String ? title.GetString() ?? string.Empty : string.Empty;
}
