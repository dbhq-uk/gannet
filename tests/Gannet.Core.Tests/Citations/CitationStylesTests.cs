using System.Globalization;
using Gannet.Core.Citations;

namespace Gannet.Core.Tests.Citations;

public class CitationStylesTests
{
    private static readonly DateOnly Accessed = new(2026, 10, 5);
    private static readonly ReferenceSource Titled = new(new Uri("https://example.com/a"), "Example Title");

    [Theory]
    [InlineData("apa", "Example Title. (n.d.). Retrieved from https://example.com/a")]
    [InlineData("mla", "\"Example Title\". Web. 05 Oct. 2026. <https://example.com/a>")]
    [InlineData("chicago", "\"Example Title.\" Accessed October 5, 2026. https://example.com/a.")]
    [InlineData("ieee", "\"Example Title,\" https://example.com/a, accessed Oct. 5, 2026.")]
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
    [InlineData("chicago", "\"Example Title.\" Accessed October 5, 2026. https://example.com/a%20b.")]
    [InlineData("ieee", "\"Example Title,\" https://example.com/a%20b, accessed Oct. 5, 2026.")]
    public void An_encoded_space_in_the_address_stays_encoded(string style, string expected)
    {
        var source = new ReferenceSource(new Uri("https://example.com/a%20b"), "Example Title");
        Assert.Equal(expected, CitationStyles.Get(style).Format(source, Accessed));
    }

    [Theory]
    [InlineData("mla", "\"example.com\". Web. 07 Mar. 2026. <https://example.com/a>")]
    [InlineData("chicago", "\"example.com.\" Accessed March 7, 2026. https://example.com/a.")]
    [InlineData("ieee", "\"example.com,\" https://example.com/a, accessed Mar. 7, 2026.")]
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

    [Theory]
    [InlineData(1, "Jan.", "January")]
    [InlineData(2, "Feb.", "February")]
    [InlineData(3, "Mar.", "March")]
    [InlineData(4, "Apr.", "April")]
    [InlineData(5, "May", "May")]
    [InlineData(6, "June", "June")]
    [InlineData(7, "July", "July")]
    [InlineData(8, "Aug.", "August")]
    [InlineData(9, "Sept.", "September")]
    [InlineData(10, "Oct.", "October")]
    [InlineData(11, "Nov.", "November")]
    [InlineData(12, "Dec.", "December")]
    public void Every_month_follows_each_styles_own_form_and_the_day_has_no_leading_zero_in_chicago_and_ieee(int month, string abbreviated, string full)
    {
        var date = new DateOnly(2026, month, 7);
        var mla = CitationStyles.Get("mla").Format(Titled, date);
        var chicago = CitationStyles.Get("chicago").Format(Titled, date);
        var ieee = CitationStyles.Get("ieee").Format(Titled, date);

        Assert.Contains($"Web. 07 {abbreviated} 2026.", mla, StringComparison.Ordinal);
        Assert.Contains($"Accessed {full} 7, 2026.", chicago, StringComparison.Ordinal);
        Assert.Contains($"accessed {abbreviated} 7, 2026.", ieee, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mla", "Web. 05 May 2026.")]
    [InlineData("chicago", "Accessed May 5, 2026.")]
    [InlineData("ieee", "accessed May 5, 2026.")]
    public void A_single_digit_day_in_may(string style, string expected)
    {
        Assert.Contains(expected, CitationStyles.Get(style).Format(Titled, new DateOnly(2026, 5, 5)), StringComparison.Ordinal);
    }

    [Fact]
    public void Month_names_do_not_depend_on_the_machines_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var date = new DateOnly(2026, 9, 9);
            Assert.Contains("09 Sept. 2026", CitationStyles.Get("mla").Format(Titled, date), StringComparison.Ordinal);
            Assert.Contains("September 9, 2026", CitationStyles.Get("chicago").Format(Titled, date), StringComparison.Ordinal);
            Assert.Contains("Sept. 9, 2026", CitationStyles.Get("ieee").Format(Titled, date), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
