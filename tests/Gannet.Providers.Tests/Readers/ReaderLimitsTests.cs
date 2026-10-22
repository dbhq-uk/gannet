using System.Diagnostics;
using System.Net;
using Gannet.Core.Services;
using Gannet.Providers.Readers;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Readers;

// The limits come from the parser itself: a cap on the elements AngleSharp creates, and a walk of the real DOM for its depth. The text pre-filter is
// bypassed here, so every shape has to be stopped by the exact checks.
public class ReaderLimitsTests
{
    private const string TooMany = "the page has too many elements to read";
    private const string TooDeep = "the page is too deeply nested to read";

    private static string Rep(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    private static Task<IPAddress[]> PublicDns(string host, CancellationToken ct) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

    private static BuiltinReader Unfiltered(string html) =>
        new(new StubHttpHandler(_ => StubHttpHandler.Text(html)).Client(), false, PublicDns, TimeSpan.FromSeconds(120), new SemaphoreSlim(1), null, null, skipPrefilter: true);

    private static Task<PageContent> Read(BuiltinReader reader) =>
        reader.ReadAsync(new Uri("https://birds.example/gannets"), 8000, CancellationToken.None);

    // 39 formatting elements with different attributes (so the parser keeps all of them), opened in a paragraph and closed by "</p>". They stay in the parser's list
    // of active formatting elements. Every later "<p>x" closes the previous paragraph and the text rebuilds all 39 as clones inside the new one: about 40 elements per
    // "<p>x", though the parser's creation callback fires only once for each. 29,000 repeats make about 1.16 million elements.
    private static string FormattingTags(string repeated, int times)
    {
        var open = string.Concat(Enumerable.Range(0, 39).Select(i => $"<b a=\"{i}\">"));
        return "<p>" + open + "</p>" + Rep(repeated, times);
    }

    public static TheoryData<string, string, string> Shapes => new()
    {
        { "</p> x40,000", Rep("</p>", 40_000), TooMany },
        { "</br> x40,000", Rep("</br>", 40_000), TooMany },
        { "39 unclosed formatting tags then <p>x x1,000", FormattingTags("<p>x", 1_000), TooMany },
        { "39 unclosed formatting tags then <div>y</div> x1,000", FormattingTags("<div>y</div>", 1_000), TooMany },
        { "<table><td></table> x14,500", Rep("<table><td></table>", 14_500), TooMany },
        { "li nesting x300", Rep("<ul><li>", 300), TooDeep },
        { "dd nesting x300", Rep("<dl><dd>", 300), TooDeep },
        { "U+00A0 before a quote", "<a b=\u00A0\">" + Rep("<div>", 300) + "\">", TooDeep },
        { "U+000B before a quote", "<a b=\u000B\">" + Rep("<div>", 300) + "\">", TooDeep },
        { "script followed by U+00A0", "<script\u00A0>" + Rep("<div>", 300), TooDeep },
        { "Kelvin sign in link", Rep("<lin\u212A>", 300), TooDeep },
        { "plain nested div", Rep("<div>", 300), TooDeep },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task The_exact_checks_refuse_each_shape_with_the_pre_filter_bypassed(string name, string html, string message)
    {
        Assert.True(name.Length > 0);
        var started = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered("<html><body>" + html)));

        Assert.Equal(message, error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    // Elements are counted as the parser creates them, so html, head and body count too: three elements plus one per <br>.
    [Theory]
    [InlineData(29_997, true)]
    [InlineData(29_998, false)]
    public async Task Exactly_30000_elements_pass_and_30001_fail(int breaks, bool passes)
    {
        var html = "<html><head></head><body>" + Rep("<br>", breaks);
        var reader = Unfiltered(html);

        if (passes)
        {
            await Read(reader);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));
            Assert.Equal(TooMany, error.Message);
        }
    }

    // html is depth 1, so html, body and 254 divs make 256.
    [Theory]
    [InlineData(254, true)]
    [InlineData(255, false)]
    public async Task Depth_256_passes_and_257_fails(int divs, bool passes)
    {
        var html = "<html><body>" + Rep("<div>", divs) + "x";
        var reader = Unfiltered(html);

        if (passes)
        {
            await Read(reader);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));
            Assert.Equal(TooDeep, error.Message);
        }
    }

    [Fact]
    public async Task A_template_hides_no_depth()
    {
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered("<html><body><template>" + Rep("<div>", 300))));
        Assert.Equal(TooDeep, error.Message);
    }

    private const string TooManyAttributes = "the page has an element with too many attributes";

    private static string Attributes(int count) => string.Concat(Enumerable.Range(0, count).Select(i => $" a{i}=1"));

    // One formatting element with 10,000 attributes, closed by "</p>" so that it stays in the list of active formatting elements, then "<p>x" repeated. The parser
    // copies the attributes onto a clone each time (the creation callback fires before attributes are set), so 2,000 repeats of a 71 KB page peaked at 2.5 GB.
    // Scaled down here: 30 repeats.
    [Fact]
    public async Task One_formatting_element_with_thousands_of_attributes_is_refused_before_it_is_cloned()
    {
        var html = "<html><body><p><b" + Attributes(10_000) + ">x</p>" + Rep("<p>x", 30);
        var started = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered(html)));

        Assert.Equal(TooManyAttributes, error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public async Task An_element_may_have_32_attributes_and_not_33_whether_or_not_more_elements_follow(int attributes, bool passes)
    {
        foreach (var html in new[] { "<html><body><div" + Attributes(attributes) + ">x</div><p>after</p>", "<html><body><div" + Attributes(attributes) + ">" })
        {
            var reader = Unfiltered(html);
            if (passes)
            {
                await Read(reader);
            }
            else
            {
                var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));
                Assert.Equal(TooManyAttributes, error.Message);
            }
        }
    }

    private static string AttributesFrom(int start, int count) => string.Concat(Enumerable.Range(start, count).Select(i => $" a{i}=1"));

    // A stray <html> or <body> tag merges its attributes onto the existing element and creates no element, so the per-element check on created elements never sees it.
    // Merging gets slower as the list grows: 3,000 rounds of 32 new attributes (838 KB) took 146 seconds. Scaled down here: 100 rounds.
    [Theory]
    [InlineData("html")]
    [InlineData("body")]
    public async Task Stray_tags_that_keep_merging_attributes_are_refused(string tag)
    {
        // A <div> comes first, so the newest created element is not the one being merged into.
        var html = "<html><body><div></div>" + string.Concat(Enumerable.Range(0, 100).Select(r => $"<{tag}{AttributesFrom(r * 32, 32)}>"));
        var started = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered(html)));

        Assert.Equal(TooManyAttributes, error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    // The error handler fires before the tag is merged, so one stray tag can bring the element to 32 or past it, and the next tag (or the check after the parse) refuses.
    [Theory]
    [InlineData("html", 32, true)]
    [InlineData("html", 33, false)]
    [InlineData("body", 32, true)]
    [InlineData("body", 33, false)]
    public async Task A_single_stray_tag_may_bring_html_or_body_to_32_attributes_and_not_33(string tag, int attributes, bool passes)
    {
        var html = $"<html><body><div></div><{tag}{AttributesFrom(0, attributes)}>x";
        var reader = Unfiltered(html);

        if (passes)
        {
            await Read(reader);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));
            Assert.Equal(TooManyAttributes, error.Message);
        }
    }

    // Text under deep nesting is very slow in SmartReader: 250 divs and one 4 MB paragraph ran for over 600 seconds, and 120 nested list levels in 197 KB took 67 seconds.
    // So the walk also adds up, for every text node, its length times its depth, and refuses past 20,000,000. Real pages: Wikipedia Gannet 481,663, Bird 3,448,386.
    [Fact]
    public async Task One_big_paragraph_under_250_divs_is_refused_fast()
    {
        var html = "<html><body>" + Rep("<div>", 250) + "<p>" + Rep("gannet ", 15_000);
        var started = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered(html)));

        Assert.Equal(TooDeep, error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    [Fact]
    public async Task One_hundred_and_twenty_nested_list_levels_with_text_are_refused_fast()
    {
        var level = "<ul><li>" + Rep("gannets dive from a great height into the sea ", 36);
        var html = "<html><body>" + Rep(level, 120);
        var started = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered(html)));

        Assert.Equal(TooDeep, error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    // Text directly in the body is at depth 3 (html 1, body 2, the text 3), so the limit falls between 6,666,666 and 6,666,667 characters. Parsed only, not read.
    [Theory]
    [InlineData(6_666_666, true)]
    [InlineData(6_666_667, false)]
    public void The_text_times_depth_limit_is_twenty_million(int characters, bool passes)
    {
        var html = "<html><head></head><body>" + new string('x', characters);

        if (passes)
        {
            Assert.NotNull(PageParser.Parse(html, "Built-in reader"));
        }
        else
        {
            var error = Assert.Throws<ServiceFailedException>(() => PageParser.Parse(html, "Built-in reader"));
            Assert.Equal(TooDeep, error.Message);
        }
    }

    // Pages inside both limits can still cost SmartReader minutes: its time grows with the sum of the element depths. 250 divs then 29,000 closed spans is 29,255
    // elements, depth 252, and a depth sum over seven million.
    [Fact]
    public async Task A_page_inside_both_limits_but_with_a_huge_total_nesting_is_refused()
    {
        var html = "<html><body>" + Rep("<div>", 250) + Rep("<span></span>", 29_000);
        var started = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered(html)));

        Assert.Equal(TooDeep, error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    // 100 nested divs, then leaves at depth 103. The depth sum is 5,255 + 103 per leaf, so the limit of 1,000,000 falls between two leaf counts.
    [Theory]
    [InlineData(9_657, true)]
    [InlineData(9_658, false)]
    public async Task The_total_nesting_limit_is_one_million(int leaves, bool passes)
    {
        const int Base = 5 + (2 * 100) + (100 * 101 / 2);
        Assert.True(Base + (103L * 9_657) <= 1_000_000 && Base + (103L * 9_658) > 1_000_000);
        var html = "<html><head></head><body>" + Rep("<div>", 100) + Rep("<br>", leaves);
        var reader = Unfiltered(html);

        if (passes)
        {
            await Read(reader);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));
            Assert.Equal(TooDeep, error.Message);
        }
    }

    // The reviewer's size. Unchecked, this builds about 1.16 million elements and uses hundreds of megabytes.
    [Fact]
    public async Task The_formatting_clone_shape_at_29000_repeats_is_stopped_early()
    {
        var started = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered("<html><body>" + FormattingTags("<p>x", 29_000))));
        Assert.Equal(TooMany, error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    [Fact]
    public async Task Parsing_stops_early_when_the_cap_is_reached()
    {
        var started = Stopwatch.StartNew();
        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Unfiltered("<html><body>" + Rep("</p>", 1_000_000))));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"took {started.Elapsed}");
    }

    // The same text pre-filter shapes, now with the filter on: the cheap fixes (HTML white space only, ASCII lower case) catch these too.
    [Theory]
    [InlineData("U+00A0 before a quote")]
    [InlineData("U+000B before a quote")]
    [InlineData("script followed by U+00A0")]
    [InlineData("Kelvin sign in link")]
    public void The_pre_filter_also_refuses_the_unicode_shapes(string name)
    {
        var html = Shapes.Select(s => (object[])s).Single(s => (string)s[0] == name)[1];
        Assert.Equal(HtmlScan.TooDeep, HtmlDepth.Scan("<html><body>" + (string)html));
    }

    [Fact]
    public async Task An_article_is_read_from_the_parsed_document_with_a_title_and_the_furniture_dropped()
    {
        const string page = "<html><head><title>Gannets dive</title></head><body><nav>Home | About</nav><article><h1>Gannets dive</h1>"
            + "<p>Northern gannets plunge into the sea from heights of up to thirty metres to catch fish. This paragraph is long enough for a readability parser to treat the article as the main content of the page.</p>"
            + "<p>They fold their wings back just before entering the water, reaching speeds of about eighty-six kilometres an hour, and air sacs in the face and chest cushion the impact.</p>"
            + "<p>Colonies on the Bass Rock in Scotland hold more than one hundred and fifty thousand birds in a good year, making it the largest northern gannet colony in the world.</p>"
            + "</article><footer>Cookie settings</footer></body></html>";
        var reader = new BuiltinReader(new StubHttpHandler(_ => StubHttpHandler.Text(page)).Client(), false, PublicDns, null, new SemaphoreSlim(1), null);

        var result = await Read(reader);

        Assert.Equal("Gannets dive", result.Title);
        Assert.Contains("thirty metres", result.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Cookie settings", result.Markdown, StringComparison.Ordinal);
    }

    // The real page, saved outside the repository. Skipped when it is not on this machine.
    [Fact]
    public async Task The_saved_wikipedia_gannet_page_is_read()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dbhq", "docs", "research", "Gannet_Build_Facts_20261021", "raw", "provider-docs", "wikipedia-gannet.html.xz");
        if (!File.Exists(path))
        {
            return;
        }

        string html;
        try
        {
            using var xz = Process.Start(new ProcessStartInfo("xz", ["-dc", path]) { RedirectStandardOutput = true, UseShellExecute = false })!;
            html = await xz.StandardOutput.ReadToEndAsync();
            await xz.WaitForExitAsync();
            if (xz.ExitCode != 0)
            {
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }

        var reader = new BuiltinReader(new StubHttpHandler(_ => StubHttpHandler.Text(html)).Client(), false, PublicDns, null, new SemaphoreSlim(1), null);

        var result = await reader.ReadAsync(new Uri("https://en.wikipedia.org/wiki/Gannet"), 8000, CancellationToken.None);

        Assert.Equal("Gannet", result.Title);
        Assert.True(result.Markdown.Length > 2000);
    }
}
