using System.Globalization;
using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Readers;

internal sealed class JinaReader(HttpClient http, string name, ServiceOptions options, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    : RemoteReaderBase(http, name, options, resolve)
{
    public override string Type => "jina";

    public override string DisplayName => "Jina Reader";

    public override ServiceTerms Terms => new(true,
        "The most permissive terms found (no restriction on output, s.4.3; no training on inputs). Keyless: 20 pages a minute per address.");

    protected override async Task<(string Title, string Markdown)> FetchAsync(Uri url, CancellationToken ct)
    {
        var endpoint = (Options.Endpoint ?? "https://r.jina.ai").TrimEnd('/');

        // The target goes into the path, with its own query string. A fragment is never sent to a server, so it is dropped (Reader README, "SPAs with hash-based routing").
        // IdnHost is the punycode form, so a unicode host is sent as ASCII.
        var port = url.IsDefaultPort ? string.Empty : $":{url.Port.ToString(CultureInfo.InvariantCulture)}";
        var target = $"{url.Scheme}://{url.IdnHost}{port}{url.PathAndQuery}";
        var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/{target}");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("X-Respond-With", "markdown");
        if (!string.IsNullOrWhiteSpace(Options.ApiKey) && !Options.Keyless)
        {
            request.Headers.Authorization = new("Bearer", Options.ApiKey);
        }

        using var json = await SendJsonAsync(request, ct);
        var data = json.RootElement.Prop("data");
        return (data.Str("title"), data.Str("content"));
    }
}
