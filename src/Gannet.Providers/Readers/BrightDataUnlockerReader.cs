using System.Globalization;
using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Readers;

/// <summary>Fetches a page through Bright Data's network, which gets past datacentre-address blocks. Opt-in only.</summary>
internal sealed class BrightDataUnlockerReader : RemoteReaderBase
{
    // The body is Markdown text, so it is read as text under a smaller limit than a JSON response.
    private const int MaxBodyBytes = 5 * 1024 * 1024;

    private readonly string zone;

    public BrightDataUnlockerReader(HttpClient http, string name, ServiceOptions options, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
        : base(http, name, options, resolve)
    {
        zone = string.IsNullOrWhiteSpace(options.Zone)
            ? throw new ArgumentException($"Bright Data needs a Web Unlocker zone: set Gannet:Readers:Services:{name}:Zone.", nameof(options))
            : options.Zone;
    }

    public override string Type => "brightdata-unlocker";

    public override string DisplayName => "Bright Data Web Unlocker";

    public override ServiceTerms Terms => new(false,
        "Bright Data \"may retain data Client has collected and may use it for its own purposes in its sole discretion\": avoid it for confidential questions.");

    // On the Direct API the outer status can be 200 when the account or zone is out of credit, so the headers are the signal.
    protected override Task<bool> IsQuotaResponseAsync(HttpResponseMessage response, CancellationToken ct) =>
        Task.FromResult(BrightDataErrors.HasQuotaCode(response));

    protected override async Task<(string Title, string Markdown)> FetchAsync(Uri url, CancellationToken ct)
    {
        var request = PostJson("https://api.brightdata.com/request", new { zone, url = url.ToString(), format = "raw", data_format = "markdown" });
        request.Headers.Authorization = new("Bearer", RequireKey());

        using (request)
        {
            using var response = await ServiceHttp.SendCheckedAsync(Http, request, DisplayName, IsQuotaResponseAsync, ct);

            // Once a request reaches the unlocker the outer status is 200. An unlock that failed says so in x-brd-error or x-brd-error-code, and the status
            // of the page itself is in x-brd-status-code. Neither is text we want to read as the page, and neither is copied into a message.
            if (response.Headers.Contains("x-brd-error") || response.Headers.Contains("x-brd-error-code"))
            {
                throw new ServiceFailedException(DisplayName, $"{DisplayName} could not unlock the page.");
            }

            if (response.Headers.TryGetValues("x-brd-status-code", out var values)
                && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var status)
                && status >= 400)
            {
                throw new ServiceFailedException(DisplayName, $"The page answered {status} to {DisplayName}.");
            }

            return (string.Empty, await JsonRead.ReadTextAsync(response, DisplayName, MaxBodyBytes, ct));
        }
    }
}
