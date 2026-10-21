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
}
