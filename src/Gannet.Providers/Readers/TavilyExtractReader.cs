using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Readers;

internal sealed class TavilyExtractReader(HttpClient http, string name, ServiceOptions options, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    : RemoteReaderBase(http, name, options, resolve)
{
    public override string Type => "tavily-extract";

    public override string DisplayName => "Tavily Extract";

    public override ServiceTerms Terms => new(false, "Keyless mode is rate limited and best effort.");

    // Documented by Tavily: 432 is the plan limit, 433 the pay-as-you-go limit.
    protected override bool IsQuotaStatus(HttpStatusCode status) => (int)status is 432 or 433;

    protected override async Task<(string Title, string Markdown)> FetchAsync(Uri url, CancellationToken ct)
    {
        var endpoint = (Options.Endpoint ?? "https://api.tavily.com").TrimEnd('/');
        var request = PostJson($"{endpoint}/extract", new { urls = new[] { url.ToString() }, format = "markdown" });

        // The key goes in the Authorization header only. With both set, Tavily would bill the key, so keyless drops it.
        if (string.IsNullOrWhiteSpace(Options.ApiKey) || Options.Keyless)
        {
            request.Headers.Add("X-Tavily-Access-Mode", "keyless");
        }
        else
        {
            request.Headers.Authorization = new("Bearer", Options.ApiKey);
        }

        using var json = await SendJsonAsync(request, ct);

        // A 200 can carry only failed_results (per-URL failures), so an empty results array is a failure, with no text from the service in the message.
        var result = json.RootElement.Prop("results").Items().FirstOrDefault();
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new ServiceFailedException(DisplayName, $"{DisplayName} could not extract the page.");
        }

        // The response has no title field.
        return (string.Empty, result.Str("raw_content"));
    }
}
