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
}
