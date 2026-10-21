using Gannet.Core.Citations;

namespace Gannet.Core.Tests.Citations;

public class CitationStylesTests
{
    private static readonly DateOnly Accessed = new(2026, 10, 5);
    private static readonly ReferenceSource Titled = new(new Uri("https://example.com/a"), "Example Title");

    [Theory]
    [InlineData("apa", "Example Title. (n.d.). Retrieved from https://example.com/a")]
    [InlineData("mla", "\"Example Title\". Web. 05 Oct. 2026. <https://example.com/a>")]
    [InlineData("chicago", "\"Example Title.\" Accessed October 05, 2026. https://example.com/a.")]
    [InlineData("ieee", "\"Example Title,\" https://example.com/a, accessed October 05, 2026.")]
    public void Formats_match_the_source(string style, string expected)
    {
        Assert.Equal(expected, CitationStyles.Get(style).Format(Titled, Accessed));
    }

    [Fact]
    public void Missing_title_uses_the_host()
    {
        var untitled = new ReferenceSource(new Uri("https://example.com/a"), " ");
        Assert.Equal("example.com. (n.d.). Retrieved from https://example.com/a", CitationStyles.Get("APA").Format(untitled, Accessed));
    }

    [Fact]
    public void Unknown_style_names_the_valid_ones()
    {
        var error = Assert.Throws<ArgumentException>(() => CitationStyles.Get("harvard"));
        Assert.Contains("apa, mla, chicago, ieee", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apa", "Example Title. (n.d.). Retrieved from https://example.com/a%20b")]
    [InlineData("mla", "\"Example Title\". Web. 05 Oct. 2026. <https://example.com/a%20b>")]
    [InlineData("chicago", "\"Example Title.\" Accessed October 05, 2026. https://example.com/a%20b.")]
    [InlineData("ieee", "\"Example Title,\" https://example.com/a%20b, accessed October 05, 2026.")]
    public void An_encoded_space_in_the_address_stays_encoded(string style, string expected)
    {
        var source = new ReferenceSource(new Uri("https://example.com/a%20b"), "Example Title");
        Assert.Equal(expected, CitationStyles.Get(style).Format(source, Accessed));
    }

    [Theory]
    [InlineData("mla", "\"example.com\". Web. 07 Mar. 2026. <https://example.com/a>")]
    [InlineData("chicago", "\"example.com.\" Accessed March 07, 2026. https://example.com/a.")]
    [InlineData("ieee", "\"example.com,\" https://example.com/a, accessed March 07, 2026.")]
    public void An_untitled_source_uses_the_host_and_dates_outside_october_format(string style, string expected)
    {
        var untitled = new ReferenceSource(new Uri("https://example.com/a"), string.Empty);
        Assert.Equal(expected, CitationStyles.Get(style).Format(untitled, new DateOnly(2026, 3, 7)));
    }

    [Fact]
    public void A_title_with_line_breaks_becomes_one_line()
    {
        var source = new ReferenceSource(new Uri("https://example.com/a"), "A\n\n[1]: https://x\n\n# Injected");
        foreach (var style in CitationStyles.All)
        {
            var line = style.Format(source, Accessed);
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\r', line);
            Assert.Contains("A [1]: https://x # Injected", line, StringComparison.Ordinal);
        }
    }
}
