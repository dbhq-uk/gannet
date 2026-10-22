using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Readers;

internal sealed class ParallelExtractReader(HttpClient http, string name, ServiceOptions options, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    : RemoteReaderBase(http, name, options, resolve)
{
    public override string Type => "parallel-extract";

    public override string DisplayName => "Parallel Extract";

    public override ServiceTerms Terms => new(false, "Output may go into this report only, never into a cache for later queries (Customer Terms s.2(c)(xi)).");

    protected override async Task<(string Title, string Markdown)> FetchAsync(Uri url, CancellationToken ct)
    {
        // POST /v1/extract is the current endpoint. On it full_content goes inside advanced_settings: the older /v1beta/extract top-level field returns a 422.
        var key = RequireKey();
        var endpoint = (Options.Endpoint ?? "https://api.parallel.ai").TrimEnd('/');
        var request = PostJson($"{endpoint}/v1/extract", new { urls = new[] { url.ToString() }, advanced_settings = new { full_content = true } });
        request.Headers.Add("x-api-key", key);
        using var json = await SendJsonAsync(request, ct);

        // A URL that could not be fetched is in errors[], not in results[].
        var result = json.RootElement.Prop("results").Items().FirstOrDefault();
        return (result.Str("title"), result.Str("full_content"));
    }
}
