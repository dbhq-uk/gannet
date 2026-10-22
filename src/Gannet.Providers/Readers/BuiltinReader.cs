using System.Net;
using System.Text;
using Gannet.Core.Services;
using Gannet.Providers.Http;
using ReverseMarkdown;

namespace Gannet.Providers.Readers;

/// <summary>
/// Fetches a page from this machine and extracts its main text. No third party sees the address. The page address comes from a search result
/// or from the model, so it is checked against <see cref="PublicAddressGuard"/> before the first request and before every redirect hop.
/// </summary>
internal sealed class BuiltinReader : IPageReader
{
    public const string UserAgent = "Mozilla/5.0 (compatible; gannet/1.0; +https://github.com/dbhq-uk/gannet)";

    private const int MaxBytes = 5_000_000;
    private const int MaxRedirects = 5;
    internal static readonly TimeSpan DefaultParseTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromSeconds(30);

    // The parser recurses, and a stack overflow cannot be caught, so the parse runs on its own thread with a stack larger than the default.
    private const int ParseStackBytes = 16 * 1024 * 1024;

    // A process-wide cap on parse workers. A parse that times out is abandoned, not stopped, so it keeps its slot until it really ends.
    internal static readonly int GateSize = Math.Max(1, Environment.ProcessorCount / 2);
    private static readonly SemaphoreSlim DefaultGate = new(GateSize, GateSize);

    private readonly HttpClient http;
    private readonly bool allowPrivateAddresses;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolve;
    private readonly TimeSpan parseTimeout;
    private readonly TimeSpan downloadTimeout;
    private readonly bool skipPrefilter;
    private readonly Func<Uri, AngleSharp.Html.Dom.IHtmlDocument, (bool Readable, string Title, string Content)> readArticle;
    private readonly SemaphoreSlim gate;
    private readonly Func<Uri, string, (string Title, string Markdown)> parse;

    static BuiltinReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public BuiltinReader(HttpClient http, bool allowPrivateAddresses = false)
        : this(http, allowPrivateAddresses, Dns.GetHostAddressesAsync, null)
    {
    }

    /// <summary>For tests: a fake name lookup, a parse time limit, a parse gate of another size, another parse step and a download deadline.</summary>
    internal BuiltinReader(
        HttpClient http,
        bool allowPrivateAddresses,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        TimeSpan? parseTimeout,
        SemaphoreSlim? gate = null,
        Func<Uri, string, (string Title, string Markdown)>? parse = null,
        TimeSpan? downloadTimeout = null,
        bool skipPrefilter = false,
        Func<Uri, AngleSharp.Html.Dom.IHtmlDocument, (bool Readable, string Title, string Content)>? readArticle = null)
    {
        this.http = http;
        this.allowPrivateAddresses = allowPrivateAddresses;
        this.resolve = resolve;
        this.parseTimeout = parseTimeout ?? DefaultParseTimeout;
        this.downloadTimeout = downloadTimeout ?? DefaultDownloadTimeout;
        this.skipPrefilter = skipPrefilter;
        this.readArticle = readArticle ?? ReadArticleWithSmartReader;
        this.gate = gate ?? DefaultGate;
        this.parse = parse ?? Extract;
    }

    public string Name => "builtin";

    public string Type => "builtin";

    public string DisplayName => "Built-in reader";

    public ServiceTerms Terms => new(true, "Fetches the page from this machine; no third party sees the address.");

    public async Task<PageContent> ReadAsync(Uri url, int maxCharacters, CancellationToken ct)
    {
        var current = url;
        for (var redirects = 0; ; redirects++)
        {
            await CheckAddressAsync(current, ct);

            // The send and the body read share one deadline, so a server that stalls cannot hold the read for ever. The parse is not under it.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(downloadTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(UserAgent);
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.5");
                request.Headers.AcceptLanguage.ParseAdd("en-GB,en;q=0.8");

                // Headers first, so the byte cap applies before HttpClient buffers a body. The response is disposed at the end of each pass.
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (response.Headers.Location is not { } location)
                    {
                        // HttpClient could not parse the header at all: say so, rather than "no Location".
                        if (response.Headers.NonValidated.Contains("Location"))
                        {
                            throw new ServiceFailedException(DisplayName, "The site redirected to an address that is not valid.");
                        }

                        throw new ServiceFailedException(DisplayName, $"The site answered {(int)response.StatusCode} without saying where the page moved to.");
                    }

                    if (redirects >= MaxRedirects)
                    {
                        throw new ServiceFailedException(DisplayName, "The page redirected too many times.");
                    }

                    // A malformed Location (for example "http:127.0.0.1") is a typed failure, never a UriFormatException.
                    if (!Uri.TryCreate(current, location, out var next))
                    {
                        throw new ServiceFailedException(DisplayName, "The site redirected to an address that is not valid.");
                    }

                    current = next;
                    continue;
                }

                var downloaded = await DownloadAsync(response, deadline.Token);
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                return await FinishAsync(current, downloaded, maxCharacters, ct);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new ServiceFailedException(DisplayName, "the page took too long to download");
            }
        }
    }

    private async Task CheckAddressAsync(Uri url, CancellationToken ct)
    {
        if (allowPrivateAddresses)
        {
            if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            {
                throw new ServiceFailedException(DisplayName, "Only http and https addresses can be read.");
            }

            return;
        }

        await PublicAddressGuard.EnsurePublicAsync(url, resolve, ct);
    }

    private sealed record Downloaded(string MediaType, bool IsHtml, string Body);

    /// <summary>Reads the body (capped) and maps an error status or a challenge to a typed failure. Everything here runs under the download deadline.</summary>
    private async Task<Downloaded> DownloadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "text/html";
        var isHtml = mediaType is "text/html" or "application/xhtml+xml";
        var isText = isHtml || mediaType == "text/plain";

        // A challenge page can come with a success or a 5xx status, so read those bodies. Other types are not read at all.
        var body = isText && (response.IsSuccessStatusCode || (int)response.StatusCode >= 500) ? await ReadCappedAsync(response, token) : string.Empty;
        if (LooksLikeChallenge(body))
        {
            throw new ChallengeDetectedException(DisplayName, "The site answered with a bot challenge page.");
        }

        await HttpErrors.EnsureSuccessAsync(response, DisplayName, website: true, token);

        if (!isText)
        {
            throw new ServiceFailedException(DisplayName, $"The page is {mediaType}, which the built-in reader does not read.");
        }

        return new Downloaded(mediaType, isHtml, body);
    }

    private async Task<PageContent> FinishAsync(Uri url, Downloaded page, int maxCharacters, CancellationToken ct)
    {
        if (!page.IsHtml)
        {
            return new PageContent(url, url.Host, TextCap.Cut(page.Body.Trim(), maxCharacters), Name);
        }

        var (title, markdown) = await ExtractAsync(url, page.Body, ct);
        return new PageContent(url, title, TextCap.Cut(markdown.Trim(), maxCharacters), Name);
    }

    private async Task<(string Title, string Markdown)> ExtractAsync(Uri url, string html, CancellationToken ct)
    {
        // A fast pre-filter: it refuses the obvious hostile shapes in milliseconds. It is not the guard. The exact limits are in PageParser.
        var scan = skipPrefilter ? HtmlScan.Ok : HtmlDepth.Scan(html);
        switch (scan)
        {
            case HtmlScan.TooDeep:
                throw new ServiceFailedException(DisplayName, "the page is too deeply nested to read");
            case HtmlScan.TooManyElements:
                throw new ServiceFailedException(DisplayName, "the page has too many elements to read");
        }

        // Wait for a slot, for as long as a parse may take. Cancellation here is a cancellation, not a "busy".
        if (!await gate.WaitAsync(parseTimeout, ct))
        {
            throw new ServiceFailedException(DisplayName, "the reader is busy; try the next reader");
        }

        // Parsing cannot be cancelled. The slot is released when the worker really ends, not when the caller gives up: on a timeout the worker
        // thread is abandoned, not stopped, so it keeps its slot and at most GateSize threads can be busy at once.
        var done = new TaskCompletionSource<(string Title, string Markdown)>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var worker = new Thread(() => RunParse(url, html, done), ParseStackBytes) { IsBackground = true, Name = "gannet-parse" };
            worker.Start();
        }
        catch
        {
            gate.Release();
            throw;
        }

        try
        {
            return await done.Task.WaitAsync(parseTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new ServiceFailedException(DisplayName, "the page took too long to parse");
        }
    }

    private void RunParse(Uri url, string html, TaskCompletionSource<(string Title, string Markdown)> done)
    {
        (string Title, string Markdown) result = default;
        Exception? failure = null;
        try
        {
            result = parse(url, html);
        }
        catch (Exception e) when (e is OperationCanceledException or ServiceFailedException)
        {
            // Our own typed refusals (too many elements, too deep) and cancellation pass through as they are.
            failure = e;
        }
        catch (Exception)
        {
            // A parser can throw on a hostile page. Its message can quote page text, so it is never passed on.
            failure = new ServiceFailedException(DisplayName, "the page could not be parsed");
        }
        finally
        {
            gate.Release();
        }

        if (failure is null)
        {
            done.SetResult(result);
        }
        else
        {
            done.SetException(failure);
        }
    }

    private (string Title, string Markdown) Extract(Uri url, string html)
    {
        // Parsed once, here, with the limits applied by the parser (PageParser). SmartReader gets the parsed document and never parses hostile HTML itself.
        var document = PageParser.Parse(html, DisplayName);

        // Read the fallback text first: SmartReader rewrites the tree it is given.
        var (text, pageTitle) = PageParser.PlainText(document);

        var (readable, articleTitle, content) = readArticle(url, document);

        // A second line of defence: an article far longer than the page it came from means the serialiser multiplied something (cloned elements, say). ReverseMarkdown
        // is not run on it.
        if (content.Length > (2L * html.Length) + 100_000)
        {
            throw new ServiceFailedException(DisplayName, "the page could not be parsed");
        }

        if (readable && !string.IsNullOrWhiteSpace(content))
        {
            var markdown = new Converter(new Config
            {
                GithubFlavored = true,
                Tags = { Unknown = Config.UnknownTagsOption.Bypass },
                Formatting = { RemoveComments = true },
                Links = { SmartHref = true },
            }).Convert(content);
            return (string.IsNullOrWhiteSpace(articleTitle) ? url.Host : articleTitle.Trim(), markdown);
        }

        return (string.IsNullOrWhiteSpace(pageTitle) ? url.Host : pageTitle.Trim(), text);
    }

    private static (bool Readable, string Title, string Content) ReadArticleWithSmartReader(Uri url, AngleSharp.Html.Dom.IHtmlDocument document)
    {
        // Not Reader.ParseArticle(uri, string): its second argument is a User-Agent, and it downloads the address itself, past the address guard and the proxy.
        // The Reader(uri, document) constructor works on the tree we parsed and makes no request.
        var article = new SmartReader.Reader(url.ToString(), document).GetArticle();
        return (article.IsReadable, article.Title ?? string.Empty, article.Content ?? string.Empty);
    }

    private static bool LooksLikeChallenge(string body) =>
        body.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Attention Required! | Cloudflare", StringComparison.OrdinalIgnoreCase)
        || body.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads at most <see cref="MaxBytes"/>; anything after that is left unread.</summary>
    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length < MaxBytes)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, MaxBytes - buffer.Length)), ct);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        Encoding encoding;
        try
        {
            var charset = response.Content.Headers.ContentType?.CharSet;
            encoding = charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }

        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF');
    }
}
