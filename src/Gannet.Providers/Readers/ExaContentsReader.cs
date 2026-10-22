using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Readers;

internal sealed class ExaContentsReader(HttpClient http, string name, ServiceOptions options, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    : RemoteReaderBase(http, name, options, resolve)
{
    public override string Type => "exa-contents";

    public override string DisplayName => "Exa contents";

    public override ServiceTerms Terms => new(false, "Terms s.4.2(a) restrict copying or displaying output without written permission; kept in memory for this report only.");

    protected override async Task<(string Title, string Markdown)> FetchAsync(Uri url, CancellationToken ct)
    {
        var key = RequireKey();
        var request = PostJson("https://api.exa.ai/contents", new { urls = new[] { url.ToString() }, text = true });
        request.Headers.Add("x-api-key", key);
        using var json = await SendJsonAsync(request, ct);

        // A URL Exa could not fetch has status "error" in statuses[] and no entry in results[], so it ends as "no text".
        var result = json.RootElement.Prop("results").Items().FirstOrDefault();
        return (result.Str("title"), result.Str("text"));
    }
}
