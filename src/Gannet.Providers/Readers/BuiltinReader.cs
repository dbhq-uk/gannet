using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
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
    internal static readonly TimeSpan DefaultParseTimeout = TimeSpan.FromSeconds(30);

    // A process-wide cap on parse workers. A parse that times out is abandoned, not stopped, so it keeps its slot until it really ends.
    internal static readonly int GateSize = Math.Max(1, Environment.ProcessorCount / 2);
    private static readonly SemaphoreSlim DefaultGate = new(GateSize, GateSize);

    private readonly HttpClient http;
    private readonly bool allowPrivateAddresses;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolve;
    private readonly TimeSpan parseTimeout;
    private readonly SemaphoreSlim gate;
    private readonly Func<Uri, string, (string Title, string Markdown)> parse;

    static BuiltinReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public BuiltinReader(HttpClient http, bool allowPrivateAddresses = false)
        : this(http, allowPrivateAddresses, Dns.GetHostAddressesAsync, null)
    {
    }

    /// <summary>For tests: a fake name lookup, a parse time limit, a parse gate of another size and another parse step.</summary>
    internal BuiltinReader(
        HttpClient http,
        bool allowPrivateAddresses,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        TimeSpan? parseTimeout,
        SemaphoreSlim? gate = null,
        Func<Uri, string, (string Title, string Markdown)>? parse = null)
    {
        this.http = http;
        this.allowPrivateAddresses = allowPrivateAddresses;
        this.resolve = resolve;
        this.parseTimeout = parseTimeout ?? DefaultParseTimeout;
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

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.5");
            request.Headers.AcceptLanguage.ParseAdd("en-GB,en;q=0.8");

            // Headers first, so the byte cap applies before HttpClient buffers a body. The response is disposed at the end of each pass.
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is not { } location)
                {
                    throw new ServiceFailedException(DisplayName, $"The site answered {(int)response.StatusCode} without saying where the page moved to.");
                }

                if (redirects >= MaxRedirects)
                {
                    throw new ServiceFailedException(DisplayName, "The page redirected too many times.");
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            return await ReadResponseAsync(current, response, maxCharacters, ct);
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

    private async Task<PageContent> ReadResponseAsync(Uri url, HttpResponseMessage response, int maxCharacters, CancellationToken ct)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "text/html";
        var isHtml = mediaType is "text/html" or "application/xhtml+xml";
        var isText = isHtml || mediaType == "text/plain";

        // A challenge page can come with a success or a 5xx status, so read those bodies. Other types are not read at all.
        var body = isText && (response.IsSuccessStatusCode || (int)response.StatusCode >= 500) ? await ReadCappedAsync(response, ct) : string.Empty;
        if (LooksLikeChallenge(body))
        {
            throw new ChallengeDetectedException(DisplayName, "The site answered with a bot challenge page.");
        }

        await HttpErrors.EnsureSuccessAsync(response, DisplayName, website: true, ct);

        if (!isText)
        {
            throw new ServiceFailedException(DisplayName, $"The page is {mediaType}, which the built-in reader does not read.");
        }

        if (!isHtml)
        {
            return new PageContent(url, url.Host, TextCap.Cut(body.Trim(), maxCharacters), Name);
        }

        var (title, markdown) = await ExtractAsync(url, body, ct);
        return new PageContent(url, title, TextCap.Cut(markdown.Trim(), maxCharacters), Name);
    }

    private async Task<(string Title, string Markdown)> ExtractAsync(Uri url, string html, CancellationToken ct)
    {
        if (HtmlDepth.Exceeds(html))
        {
            throw new ServiceFailedException(DisplayName, "the page is too deeply nested to read");
        }

        // Wait for a slot, for as long as a parse may take. Cancellation here is a cancellation, not a "busy".
        if (!await gate.WaitAsync(parseTimeout, ct))
        {
            throw new ServiceFailedException(DisplayName, "the reader is busy; try the next reader");
        }

        // Parsing cannot be cancelled. The slot is released when the worker really ends, not when the caller gives up: on a timeout the worker
        // thread is abandoned, not stopped, so it keeps its slot and at most GateSize threads can be busy at once. The worker is not given ct,
        // because a worker that never starts would never release the slot.
        Task<(string Title, string Markdown)> worker;
        try
        {
            worker = Task.Run(() =>
            {
                try
                {
                    return parse(url, html);
                }
                finally
                {
                    gate.Release();
                }
            });
        }
        catch
        {
            gate.Release();
            throw;
        }

        try
        {
            return await worker.WaitAsync(parseTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new ServiceFailedException(DisplayName, "the page took too long to parse");
        }
    }

    private static (string Title, string Markdown) Extract(Uri url, string html)
    {
        // Not Reader.ParseArticle(uri, string): its second argument is a User-Agent, and it downloads the address itself, past the address guard and the proxy.
        // The Reader(uri, text) constructor parses the text we fetched and makes no request.
        var article = new SmartReader.Reader(url.ToString(), html).GetArticle();
        if (article.IsReadable && !string.IsNullOrWhiteSpace(article.Content))
        {
            var markdown = new Converter(new Config
            {
                GithubFlavored = true,
                Tags = { Unknown = Config.UnknownTagsOption.Bypass },
                Formatting = { RemoveComments = true },
                Links = { SmartHref = true },
            }).Convert(article.Content);
            return (string.IsNullOrWhiteSpace(article.Title) ? url.Host : article.Title.Trim(), markdown);
        }

        var (text, title) = PlainText(html);
        return (string.IsNullOrWhiteSpace(title) ? url.Host : title.Trim(), text);
    }

    private static bool LooksLikeChallenge(string body) =>
        body.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Attention Required! | Cloudflare", StringComparison.OrdinalIgnoreCase)
        || body.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase);

    /// <summary>The fallback for a page the readability parser gives up on: the body text without scripts, menus and footers, and the page title.</summary>
    private static (string Text, string Title) PlainText(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var title = document.Title ?? string.Empty;
        foreach (var element in document.QuerySelectorAll("script, style, nav, header, footer, noscript, textarea, template").ToList())
        {
            element.Remove();
        }

        var text = document.Body?.TextContent ?? string.Empty;
        return (string.Join("\n\n", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)), title);
    }

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
