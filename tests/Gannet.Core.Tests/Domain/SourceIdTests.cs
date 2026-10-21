using System.Text.Json;
using Gannet.Core.Domain;
using Gannet.Core.Json;

namespace Gannet.Core.Tests.Domain;

public class SourceIdTests
{
    [Theory]
    [InlineData("S1", 1)]
    [InlineData("S42", 42)]
    [InlineData("S100000", 100000)]
    public void TryParse_accepts_S_followed_by_digits(string text, int expected)
    {
        Assert.True(SourceId.TryParse(text, out var id));
        Assert.Equal(expected, id.Number);
        Assert.Equal(text, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S")]
    [InlineData("s1")]
    [InlineData("S0")]
    [InlineData("S01")]
    [InlineData("S-1")]
    [InlineData(" S1")]
    [InlineData("S1 ")]
    [InlineData("1")]
    [InlineData("S1a")]
    [InlineData("S12345678")]
    public void TryParse_rejects_anything_else(string? text)
    {
        Assert.False(SourceId.TryParse(text, out _));
    }

    [Fact]
    public void Serialises_as_its_text_form()
    {
        var json = JsonSerializer.Serialize(new SourceId(7), GannetJson.Options);
        Assert.Equal("\"S7\"", json);
        Assert.Equal(new SourceId(7), JsonSerializer.Deserialize<SourceId>(json, GannetJson.Options));
    }
}
