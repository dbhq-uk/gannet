using System.Diagnostics;
using System.Net;
using System.Text;
using Gannet.Core.Services;
using Gannet.Providers.Readers;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Readers;

public class BuiltinReaderTests
{
    private const string Article = """
        <html><head><title>Gannets dive</title></head><body>
        <nav>Home | About | Contact</nav>
        <article><h1>Gannets dive</h1>
        <p>Northern gannets plunge into the sea from heights of up to thirty metres to catch fish. This paragraph is long enough for a readability parser to treat the article as the main content of the page.</p>
        <p>They fold their wings back just before entering the water, reaching speeds of about eighty-six kilometres an hour, and air sacs in the face and chest cushion the impact.</p>
        <p>Colonies on the Bass Rock in Scotland hold more than one hundred and fifty thousand birds in a good year, making it the largest northern gannet colony in the world.</p>
        </article><footer>Cookie settings</footer></body></html>
        """;

    // Host names in these tests resolve through a fake lookup, so no test touches the network.
    private static Task<IPAddress[]> PublicDns(string host, CancellationToken ct) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

    private static BuiltinReader Reader(StubHttpHandler handler, bool allowPrivate = false, TimeSpan? parseTimeout = null) =>
        new(handler.Client(), allowPrivate, PublicDns, parseTimeout);

    private static Task<PageContent> Read(BuiltinReader reader, string url = "https://birds.example/gannets", int max = 8000) =>
        reader.ReadAsync(new Uri(url), max, CancellationToken.None);

    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }

    [Fact]
    public async Task Extracts_main_text_as_markdown()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var page = await Read(Reader(handler));

        Assert.Equal("builtin", page.Reader);
        Assert.Contains("thirty metres", page.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Cookie settings", page.Markdown, StringComparison.Ordinal);
        Assert.Contains("gannet", page.Title, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Mozilla/5.0 (compatible; gannet", Assert.Single(handler.Requests).Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_public_constructor_works_for_a_public_address_literal()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var page = await new BuiltinReader(handler.Client()).ReadAsync(new Uri("https://93.184.216.34/gannets"), 8000, CancellationToken.None);
        Assert.Contains("thirty metres", page.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caps_the_text()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var page = await Read(Reader(handler), max: 120);
        Assert.True(page.Markdown.Length <= 120);
    }

    [Fact]
    public async Task Plain_text_passes_through()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("just text", "text/plain"));
        var page = await Read(Reader(handler), "https://x.example/a.txt");
        Assert.Equal("just text", page.Markdown);
    }

    [Fact]
    public async Task A_page_the_parser_calls_unreadable_falls_back_to_its_text_without_the_furniture()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html><body><nav>Menu</nav><p>Short note about gannets.</p><footer>Cookie settings</footer></body></html>"));
        var page = await Read(Reader(handler));
        Assert.Contains("Short note about gannets.", page.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Cookie settings", page.Markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "<html>blocked</html>")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html><head><title>Just a moment...</title></head></html>")]
    [InlineData(HttpStatusCode.OK, "<html><head><title>Just a moment...</title></head></html>")]
    public async Task Blocks_and_challenges_are_typed(HttpStatusCode status, string body)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(body, status: status));
        await Assert.ThrowsAsync<ChallengeDetectedException>(() => Read(Reader(handler), "https://x.example/"));
    }

    [Fact]
    public async Task A_missing_page_is_a_typed_failure_without_the_body()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html>secret-body-text</html>", status: HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler)));
        Assert.DoesNotContain("secret-body-text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_content_types_are_refused_before_the_body_is_read()
    {
        var body = new CountingStream(1_000_000);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) { Headers = { ContentType = new("application/pdf") } } });
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler), "https://x.example/a.pdf"));
        Assert.Contains("application/pdf", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, body.BytesRead);
        Assert.True(body.Disposed);
    }

    [Fact]
    public async Task A_body_with_no_length_is_cut_off_at_the_byte_cap_and_the_response_is_disposed()
    {
        var body = new CountingStream(64 * 1024 * 1024);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) { Headers = { ContentType = new("text/plain") } } });

        await Read(Reader(handler), "https://x.example/big.txt");

        Assert.True(body.BytesRead <= (5 * 1000 * 1000) + (256 * 1024), $"read {body.BytesRead} bytes");
        Assert.True(body.Disposed);
    }

    // Redirects: the client does not follow them, so the reader does, checking every hop.
    [Fact]
    public async Task A_redirect_to_loopback_is_refused_and_never_requested()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host == "birds.example" ? Redirect("http://127.0.0.1/admin") : StubHttpHandler.Text(Article));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler)));
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("127.0.0.1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redirect_to_a_name_that_resolves_privately_is_refused()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host == "birds.example" ? Redirect("http://rebind.example/") : StubHttpHandler.Text(Article));
        var reader = new BuiltinReader(handler.Client(), false, (host, _) => Task.FromResult(new[] { IPAddress.Parse(host == "rebind.example" ? "10.0.0.7" : "93.184.216.34") }), null);
        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_first_address_is_checked_before_any_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler), "http://169.254.169.254/latest/meta-data/"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_chain_of_six_redirects_is_refused()
    {
        var handler = new StubHttpHandler(r => int.TryParse(r.RequestUri!.AbsolutePath.TrimStart('/'), out var n) && n < 6
            ? Redirect($"/{n + 1}")
            : StubHttpHandler.Text(Article));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler), "https://birds.example/0"));
        Assert.Contains("redirect", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_chain_of_five_redirects_is_followed_and_every_intermediate_response_is_disposed()
    {
        var streams = new List<CountingStream>();
        var handler = new StubHttpHandler(r =>
        {
            if (int.TryParse(r.RequestUri!.AbsolutePath.TrimStart('/'), out var n) && n < 5)
            {
                var stream = new CountingStream(10);
                streams.Add(stream);
                var response = Redirect($"/{n + 1}");
                response.Content = new StreamContent(stream);
                return response;
            }

            return StubHttpHandler.Text(Article);
        });

        var page = await Read(Reader(handler), "https://birds.example/0");

        Assert.Contains("thirty metres", page.Markdown, StringComparison.Ordinal);
        Assert.Equal(6, handler.Requests.Count);
        Assert.Equal(5, streams.Count);
        Assert.All(streams, s => Assert.True(s.Disposed));
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task A_relative_redirect_is_followed_and_the_final_address_is_reported(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.AbsolutePath == "/old/page" ? Redirect("../new/page?x=1", status) : StubHttpHandler.Text(Article));
        var page = await Read(Reader(handler), "https://birds.example/old/page");
        Assert.Equal(new Uri("https://birds.example/new/page?x=1"), page.Url);
        Assert.Equal(new Uri("https://birds.example/new/page?x=1"), handler.Requests[1].Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_redirect_to_another_scheme_stops(bool allowPrivate)
    {
        var handler = new StubHttpHandler(_ => Redirect("ftp://files.example/gannets.txt"));
        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler, allowPrivate)));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_redirect_with_no_location_is_a_failure()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found));
        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler)));
    }

    [Fact]
    public async Task Allowing_private_addresses_lets_a_loopback_address_through()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var page = await Read(Reader(handler, allowPrivate: true), "http://127.0.0.1:8080/gannets");
        Assert.Contains("thirty metres", page.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allowing_private_addresses_does_not_need_a_lookup()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var reader = new BuiltinReader(handler.Client(), true, (_, _) => throw new InvalidOperationException("no lookup"), null);
        await Read(reader, "http://intranet.example/gannets");
    }

    // SmartReader's two-string ParseArticle(uri, userAgent) downloads the address itself, with the second string as the User-Agent. The reader
    // must hand it the text and never let it fetch anything.
    [Fact]
    public async Task Parsing_never_makes_a_request_of_its_own()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();

        // Words only, so that it is a legal header value: the wrong call sends the page text as the User-Agent, and fetches.
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("Gannets plunge into the sea."));

        var page = await Read(Reader(handler, allowPrivate: true, parseTimeout: TimeSpan.FromSeconds(5)), $"http://127.0.0.1:{port}/gannets");

        await Task.Delay(500);
        Assert.False(accepted.IsCompleted, "something connected to the page address besides the injected client");
        Assert.Contains("plunge", page.Markdown, StringComparison.Ordinal);
        listener.Stop();
    }

    [Fact]
    public async Task A_readable_article_is_converted_from_the_text_we_fetched()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var page = await Read(Reader(handler));
        Assert.Equal("Gannets dive", page.Title);
        Assert.DoesNotContain("Home | About", page.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", page.Markdown, StringComparison.Ordinal);
    }

    // Hostile HTML: refused before any parser sees it.
    private static string Nest(string tag, int count) => string.Concat(Enumerable.Repeat($"<{tag}>", count));

    [Theory]
    [InlineData("div")]
    [InlineData("b")]
    [InlineData("span")]
    [InlineData("table")]
    public async Task Pages_nested_deeper_than_the_limit_are_refused_fast(string tag)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html><body>" + Nest(tag, 20_000)));
        var started = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler)));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), $"took {started.Elapsed}");
        Assert.Equal("the page is too deeply nested to read", error.Message);
    }

    [Fact]
    public async Task Self_closing_tags_outside_foreign_content_still_nest()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html><body>" + string.Concat(Enumerable.Repeat("<div/>", 1500))));
        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler)));
    }

    [Fact]
    public async Task Stray_end_tags_do_not_reset_the_depth()
    {
        var round = Nest("div", 600) + string.Concat(Enumerable.Repeat("</b>", 600));
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html><body>" + round + round + round));
        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler)));
    }

    [Theory]
    [InlineData(256, false)]
    [InlineData(257, true)]
    public void The_depth_limit_is_256_open_elements(int depth, bool refused) =>
        Assert.Equal(refused, HtmlDepth.Exceeds("<html><body>" + Nest("div", depth - 2) + "x"));

    [Fact]
    public async Task A_deep_but_ordinary_page_is_still_read()
    {
        var html = "<html><body>" + Nest("div", 100) + "<p>Gannets dive.</p>" + string.Concat(Enumerable.Repeat("</div>", 100)) + "</body></html>";
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(html));
        var page = await Read(Reader(handler));
        Assert.Contains("Gannets dive.", page.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_large_flat_page_with_many_closed_tags_is_read()
    {
        var rows = string.Concat(Enumerable.Repeat("<li><a href=\"/x\">link</a> <span>text</span></li>", 4000));
        var html = $"<html><head><title>Big</title></head><body><article><p>{string.Concat(Enumerable.Repeat("Gannets dive from a height. ", 40))}</p></article><ul>{rows}</ul></body></html>";
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(html));
        var started = Stopwatch.StartNew();
        var page = await Read(Reader(handler));
        Assert.Contains("Gannets dive", page.Markdown, StringComparison.Ordinal);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), $"took {started.Elapsed}");
    }

    [Fact]
    public void Unclosed_paragraphs_and_list_items_do_not_count_as_nesting() =>
        Assert.False(HtmlDepth.Exceeds("<html><body>" + string.Concat(Enumerable.Repeat("<p>Gannets dive.", 5000)) + string.Concat(Enumerable.Repeat("<li>Gannets", 5000)) + string.Concat(Enumerable.Repeat("<td>x<tr>", 5000))));

    [Fact]
    public async Task Self_closing_svg_shapes_do_not_count_as_nesting()
    {
        Assert.False(HtmlDepth.Exceeds("<html><body><svg>" + string.Concat(Enumerable.Repeat("<path d=\"M0 0\"/>", 5000)) + "</svg></body></html>"));

        var html = "<html><body><svg>" + string.Concat(Enumerable.Repeat("<path d=\"M0 0\"/>", 300)) + "</svg><p>Gannets dive.</p></body></html>";
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(html));
        var page = await Read(Reader(handler));
        Assert.Contains("Gannets dive.", page.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tags_inside_scripts_styles_comments_and_quoted_attributes_do_not_count()
    {
        var html = "<html><body><script>" + Nest("div", 5000) + "</script><style>" + Nest("div", 5000) + "</style><textarea>" + Nest("div", 5000) + "</textarea><!--" + Nest("div", 5000) + "-->"
            + "<a title=\"" + Nest("div", 5000) + "\">link</a><p>Gannets dive.</p></body></html>";
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(html));
        var page = await Read(Reader(handler));
        Assert.Contains("Gannets dive.", page.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_parse_that_takes_too_long_is_a_typed_failure()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Reader(handler, parseTimeout: TimeSpan.Zero)));
        Assert.Equal("the page took too long to parse", error.Message);
    }

    [Fact]
    public void The_defaults_are_a_thirty_second_parse_limit_and_a_gate_sized_to_half_the_processors()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), BuiltinReader.DefaultParseTimeout);
        Assert.Equal(Math.Max(1, Environment.ProcessorCount / 2), BuiltinReader.GateSize);
    }

    // The parse gate: a process-wide cap on parse workers. A parse that timed out is abandoned, not stopped, so it keeps its slot until it ends.
    private sealed class BlockingParse
    {
        private readonly ManualResetEventSlim release = new(false);
        private readonly SemaphoreSlim entered = new(0);

        public int Calls { get; private set; }

        public Task Entered => entered.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release() => release.Set();

        public (string Title, string Markdown) Parse(Uri url, string html)
        {
            Calls++;
            entered.Release();
            release.Wait(TimeSpan.FromSeconds(30));
            return ("Blocked", "text");
        }
    }

    private static BuiltinReader Gated(SemaphoreSlim gate, BlockingParse parse, TimeSpan timeout) =>
        new(new StubHttpHandler(_ => StubHttpHandler.Text(Article)).Client(), false, PublicDns, timeout, gate, parse.Parse);

    [Fact]
    public async Task A_second_read_waits_for_the_slot_then_fails_busy()
    {
        var gate = new SemaphoreSlim(1);
        var parse = new BlockingParse();
        var reader = Gated(gate, parse, TimeSpan.FromMilliseconds(600));

        var first = Read(reader);
        await parse.Entered;
        var second = Read(reader);
        await Task.Delay(200);
        Assert.False(second.IsCompleted, "the second read should be waiting for the slot");

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => second);
        Assert.Equal("the reader is busy; try the next reader", error.Message);
        Assert.Equal(1, parse.Calls);

        await Assert.ThrowsAsync<ServiceFailedException>(() => first);
        parse.Release();
    }

    [Fact]
    public async Task A_timed_out_parse_keeps_its_slot_until_its_worker_ends()
    {
        var gate = new SemaphoreSlim(1);
        var parse = new BlockingParse();
        var reader = Gated(gate, parse, TimeSpan.FromMilliseconds(300));

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));
        Assert.Equal("the page took too long to parse", error.Message);
        Assert.Equal(0, gate.CurrentCount);

        parse.Release();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (gate.CurrentCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task A_slot_is_released_after_a_normal_parse_and_after_a_failed_one()
    {
        var gate = new SemaphoreSlim(1);
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        var good = new BuiltinReader(handler.Client(), false, PublicDns, null, gate, null);
        await Read(good);
        Assert.Equal(1, gate.CurrentCount);

        var bad = new BuiltinReader(handler.Client(), false, PublicDns, null, gate, (_, _) => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Read(bad));
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_a_slot_is_a_cancellation()
    {
        var gate = new SemaphoreSlim(1);
        var parse = new BlockingParse();
        var reader = Gated(gate, parse, TimeSpan.FromSeconds(30));

        var first = Read(reader);
        await parse.Entered;
        using var cts = new CancellationTokenSource();
        var second = reader.ReadAsync(new Uri("https://birds.example/g"), 8000, cts.Token);
        await Task.Delay(200);
        Assert.False(second.IsCompleted);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(1, parse.Calls);
        parse.Release();
        await first;
    }

    [Fact]
    public async Task Cancellation_while_parsing_is_a_cancellation_not_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Article));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(handler).ReadAsync(new Uri("https://birds.example/g"), 8000, cts.Token));
    }

    [Fact]
    public async Task A_declared_charset_is_honoured()
    {
        var bytes = Encoding.Latin1.GetBytes("<html><head><title>Café</title></head><body><p>Café gannets.</p></body></html>");
        var content = new ByteArrayContent(bytes) { Headers = { ContentType = new("text/html") { CharSet = "iso-8859-1" } } };
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var page = await Read(Reader(handler));
        Assert.Contains("Café gannets.", page.Markdown, StringComparison.Ordinal);
    }
}
