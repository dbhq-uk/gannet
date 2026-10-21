using Gannet.Providers.Http;

namespace Gannet.Providers.Tests.Http;

public class TextCapTests
{
    [Fact]
    public void Short_text_is_unchanged() => Assert.Equal("abc", TextCap.Cut("abc", 10));

    [Fact]
    public void Cuts_at_the_last_paragraph_break_before_the_cap()
    {
        var text = new string('a', 60) + "\n\n" + new string('b', 60);
        Assert.Equal(new string('a', 60), TextCap.Cut(text, 100));
    }

    [Fact]
    public void Falls_back_to_the_last_space_then_a_hard_cut()
    {
        Assert.Equal("one two", TextCap.Cut("one two three", 10));
        Assert.Equal("abcde", TextCap.Cut("abcdefghij", 5));
    }

    [Fact]
    public void A_cut_never_ends_on_half_an_emoji()
    {
        // The emoji is two UTF-16 units; a cap of 9 would keep only the first.
        Assert.Equal("abcdefgh", TextCap.Cut("abcdefgh\uD83D\uDE00ijkl", 9));
        Assert.Equal("ab", TextCap.Cut("ab\uD83D\uDE00cd", 3));
    }

    [Fact]
    public void A_cut_after_a_whole_emoji_keeps_it() =>
        Assert.Equal("abcdefg\uD83D\uDE00", TextCap.Cut("abcdefg\uD83D\uDE00ijkl", 9));

    [Fact]
    public void A_negative_cap_is_treated_as_zero()
    {
        Assert.Equal(string.Empty, TextCap.Cut("abc", -5));
        Assert.Equal(string.Empty, TextCap.Cut("abc", 0));
        Assert.Equal(string.Empty, TextCap.Cut(string.Empty, -1));
    }

    [Fact]
    public void A_space_cut_that_loses_more_than_half_is_not_used() =>
        Assert.Equal("a bcdefghi", TextCap.Cut("a bcdefghijklmn", 10));

    [Fact]
    public void A_paragraph_cut_that_loses_more_than_half_is_not_used() =>
        Assert.Equal("a\n\nbcdefgh", TextCap.Cut("a\n\nbcdefghijklm", 10));

    [Fact]
    public void A_paragraph_too_early_falls_back_to_a_late_space() =>
        Assert.Equal("a\n\nbcd efg", TextCap.Cut("a\n\nbcd efg hijk", 12));

    [Fact]
    public void A_windows_paragraph_break_counts()
    {
        var text = new string('a', 60) + "\r\n\r\n" + new string('b', 60);
        Assert.Equal(new string('a', 60), TextCap.Cut(text, 100));
    }

    [Fact]
    public void Text_exactly_at_the_cap_is_unchanged() => Assert.Equal("abcde", TextCap.Cut("abcde", 5));
}
