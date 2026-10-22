using Gannet.Providers.Readers;

namespace Gannet.Providers.Tests.Readers;

public class HtmlDepthTests
{
    private static string Rep(string tag, int count) => string.Concat(Enumerable.Repeat(tag, count));

    // Each shape tries to make the scan think the page is flat while the parser nests it. Every one must be refused at n = 300.
    public static TheoryData<string, string> Bypasses => new()
    {
        { "svg script", "<svg><script>" + Rep("<div>", 300) },
        { "svg style", "<svg><style>" + Rep("<div>", 300) },
        { "svg title", "<svg><title>" + Rep("<div>", 300) },
        { "svg textarea", "<svg><textarea>" + Rep("<div>", 300) },
        { "math style", "<math><style>" + Rep("<div>", 300) },
        { "svg b then self-closing divs", "<svg><b>" + Rep("<div/>", 300) },
        { "svg p then self-closing divs", "<svg><p>" + Rep("<div/>", 300) },
        { "span with ignored end tags", Rep("<span>" + Rep("<div>", 200) + "</span>", 300) },
        { "equals sign opens an attribute name", "<a =\"x>" + Rep("<div>", 300) + "\">" },
        { "comment ended by --!>", "<!-- --!>" + Rep("<div>", 300) + "-->" },
        { "rt", Rep("<rt>", 300) },
        { "rp", Rep("<rp>", 300) },
        { "optgroup", Rep("<optgroup>", 300) },
        { "command", Rep("<command>", 300) },
        { "div", Rep("<div>", 300) },
        { "self-closing div", Rep("<div/>", 300) },
    };

    [Theory]
    [MemberData(nameof(Bypasses))]
    public void Each_bypass_shape_is_refused_at_300(string name, string html)
    {
        Assert.True(name.Length > 0);
        Assert.Equal(HtmlScan.TooDeep, HtmlDepth.Scan("<html><body>" + html));
    }

    [Theory]
    [MemberData(nameof(Bypasses))]
    public void Each_bypass_shape_is_refused_at_a_megabyte_in_milliseconds(string name, string html)
    {
        Assert.True(name.Length > 0);
        var big = "<html><body>" + html + Rep(html, 1_000_000 / Math.Max(1, html.Length));

        // The bound is wide on purpose: a busy machine must not fail this, and a quadratic scan of a megabyte would take hours.
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.NotEqual(HtmlScan.Ok, HtmlDepth.Scan(big));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(60), $"took {started.Elapsed}");
    }

    [Fact]
    public void Raw_text_is_skipped_outside_foreign_content()
    {
        Assert.Equal(HtmlScan.Ok, HtmlDepth.Scan("<html><body><script>" + Rep("<div>", 5000) + "</script><style>" + Rep("<div>", 5000) + "</style><title>" + Rep("<div>", 5000) + "</title></body></html>"));
    }

    [Fact]
    public void An_end_tag_closes_what_is_above_it_when_nothing_special_is_in_the_way()
    {
        // </div> closes the open <b>, so the depth never grows.
        Assert.Equal(HtmlScan.Ok, HtmlDepth.Scan("<html><body>" + Rep("<div><b><i></div>", 5000)));
    }

    [Fact]
    public void An_end_tag_is_ignored_when_a_special_element_is_above_its_match()
    {
        Assert.Equal(HtmlScan.TooDeep, HtmlDepth.Scan(Rep("<span><div></span>", 300)));
    }

    [Theory]
    [InlineData("<!-->")]
    [InlineData("<!--->")]
    [InlineData("<!---->")]
    [InlineData("<!-- a -- b -->")]
    [InlineData("<!-- a --!>")]
    public void Comments_end_where_the_parser_ends_them(string comment) =>
        Assert.Equal(HtmlScan.TooDeep, HtmlDepth.Scan(comment + Rep("<div>", 300)));

    [Fact]
    public void A_quote_after_a_name_and_equals_hides_a_greater_than_sign()
    {
        Assert.Equal(HtmlScan.Ok, HtmlDepth.Scan("<a title=\"" + Rep("<div>", 5000) + "\">x</a>"));
        Assert.Equal(HtmlScan.Ok, HtmlDepth.Scan("<a title='" + Rep("<div>", 5000) + "'>x</a>"));
    }

    [Theory]
    [InlineData(30_000, false)]
    [InlineData(30_001, true)]
    public void The_element_cap_is_30000_start_tags(int count, bool refused) =>
        Assert.Equal(refused ? HtmlScan.TooManyElements : HtmlScan.Ok, HtmlDepth.Scan(Rep("<br>", count)));

    [Fact]
    public void Tags_in_text_that_are_not_tags_are_not_counted()
    {
        Assert.Equal(HtmlScan.Ok, HtmlDepth.Scan(Rep("1 < 2 and 3 <4 ", 50_000)));
    }
}
