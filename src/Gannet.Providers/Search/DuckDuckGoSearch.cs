using System.Net;
using AngleSharp.Html.Parser;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

/// <summary>
/// The labelled last resort: DuckDuckGo's HTML page. No API terms, robots.txt disallows /html, and it often
/// challenges datacentre addresses. One request at a time, at least a second apart, no proxy rotation.
/// </summary>
internal sealed class DuckDuckGoSearch : SearchProviderBase
{
    // The page is read with a small cap and refused if it has more than this many tags, because AngleSharp's parse time grows much faster than the size
    // of a hostile page (100 KB of nested <div> took 17 s). A real results page is about 27 KB.
    private const int MaxPageBytes = 512 * 1024;
    private const int MaxTags = 5000;

    // The public constructor shares one gate and one timestamp across the process, so every instance together makes one request at a time.
    private static readonly Pacing Shared = new();

    private readonly Pacing pacing;
    private readonly TimeProvider time;
    private readonly TimeSpan minimumGap;

    public DuckDuckGoSearch(HttpClient http, string name, ServiceOptions options)
        : this(http, name, options, TimeProvider.System, TimeSpan.FromSeconds(1), Shared)
    {
    }

    // For tests: its own gate and timestamp, so a test clock cannot hold back another test.
    internal DuckDuckGoSearch(HttpClient http, string name, ServiceOptions options, TimeProvider time, TimeSpan minimumGap)
        : this(http, name, options, time, minimumGap, new Pacing())
    {
    }

    private DuckDuckGoSearch(HttpClient http, string name, ServiceOptions options, TimeProvider time, TimeSpan minimumGap, Pacing pacing)
        : base(http, name, options)
    {
        this.time = time;
        this.minimumGap = minimumGap;
        this.pacing = pacing;
    }

    public override string Type => "duckduckgo";

    public override string DisplayName => "DuckDuckGo (last resort)";

    public override ServiceTerms Terms => new(false,
        "Last resort only: no API terms, robots.txt disallows /html, and requests from datacentre addresses are often challenged.");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        string html;
        await pacing.Gate.WaitAsync(ct);
        try
        {
            // Never wait longer than the gap: the shared timestamp can come from a different clock.
            var wait = pacing.LastRequest + minimumGap - time.GetUtcNow();
            if (wait > minimumGap)
            {
                wait = minimumGap;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, ct);
            }

            pacing.LastRequest = time.GetUtcNow();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://html.duckduckgo.com/html/")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("q", query)]),
            };
            request.Headers.UserAgent.ParseAdd("gannet/1.0 (+https://github.com/dbhq-uk/gannet)");
            request.Headers.Accept.ParseAdd("text/html");

            // Headers first, so the cap applies before the body is buffered.
            using var response = await ServiceHttp.SendAsync(Http, request, DisplayName, ct);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                throw new ChallengeDetectedException(DisplayName, "DuckDuckGo answered with a bot challenge.");
            }

            await HttpErrors.EnsureSuccessAsync(response, DisplayName, website: true, ct);
            html = await JsonRead.ReadTextAsync(response, DisplayName, MaxPageBytes, ct, BodyDeadline);
        }
        finally
        {
            pacing.Gate.Release();
        }

        // Parsing happens outside the gate: only the request timing needs the lock.
        if (html.AsSpan().Count('<') > MaxTags)
        {
            throw new ServiceFailedException(DisplayName, "DuckDuckGo returned a page with too much markup to read safely.");
        }

        var hits = Parse(html, maxResults);

        // The challenge text counts only when there are no results, so a result that mentions it is still a result.
        if (hits.Count == 0 && html.Contains("bots use DuckDuckGo too", StringComparison.OrdinalIgnoreCase))
        {
            throw new ChallengeDetectedException(DisplayName, "DuckDuckGo answered with a bot challenge.");
        }

        return hits;
    }

    private static List<SearchHit> Parse(string html, int maxResults)
    {
        var document = new HtmlParser().ParseDocument(html);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hits = new List<SearchHit>();
        foreach (var result in document.QuerySelectorAll(".result"))
        {
            if (result.ClassList.Contains("result--ad"))
            {
                continue;
            }

            var link = result.QuerySelector("a.result__a");
            var url = Unwrap(link?.GetAttribute("href"));
            if (link is null || url is null || IsDuckDuckGo(url.Host) || !seen.Add(url.AbsoluteUri))
            {
                continue;
            }

            hits.Add(new SearchHit(link.TextContent.Trim(), url, result.QuerySelector(".result__snippet")?.TextContent.Trim() ?? string.Empty));
            if (hits.Count == maxResults)
            {
                break;
            }
        }

        return hits;
    }

    private static Uri? Unwrap(string? href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }

        var absolute = href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
        var uri = JsonRead.WebUri(absolute);
        if (uri is not null && IsDuckDuckGo(uri.Host) && uri.AbsolutePath.StartsWith("/l/", StringComparison.Ordinal))
        {
            // The target must pass the same check as any result URL: http or https, no userinfo.
            return JsonRead.WebUri(System.Web.HttpUtility.ParseQueryString(uri.Query)["uddg"]);
        }

        return uri;
    }

    private static bool IsDuckDuckGo(string host) =>
        host.Equals("duckduckgo.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".duckduckgo.com", StringComparison.OrdinalIgnoreCase);

    private sealed class Pacing
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTimeOffset LastRequest { get; set; } = DateTimeOffset.MinValue;
    }
}
