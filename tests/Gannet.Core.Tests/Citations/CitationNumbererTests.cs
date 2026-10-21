using Gannet.Core.Citations;
using Gannet.Core.Domain;

namespace Gannet.Core.Tests.Citations;

public class CitationNumbererTests
{
    private static readonly DateOnly Accessed = new(2026, 10, 21);

    private static Source S(int n) => new(new SourceId(n), new Uri($"https://example.com/{n}"), $"Title {n}", "test", "q", 0);

    [Fact]
    public void Numbers_sources_in_order_of_first_use_across_sections()
    {
        ReportSection[] sections = [new("A", "A [S3]. B [S1]."), new("B", "C [S3][S2].")];
        var numbered = CitationNumberer.Number(sections, [S(1), S(2), S(3)], CitationStyles.Get("apa"), Accessed);

        Assert.Equal("A [1]. B [2].", numbered.Sections[0].Markdown);
        Assert.Equal("C [1][3].", numbered.Sections[1].Markdown);
        Assert.Equal([new SourceId(3), new SourceId(1), new SourceId(2)], numbered.References.Select(r => r.Source));
        Assert.Equal([1, 2, 3], numbered.References.Select(r => r.Number));
        Assert.Equal("Title 3. (n.d.). Retrieved from https://example.com/3", numbered.References[0].Formatted);
    }

    [Fact]
    public void Sources_never_cited_get_no_reference()
    {
        var numbered = CitationNumberer.Number([new("A", "No citations here.")], [S(1)], CitationStyles.Get("apa"), Accessed);
        Assert.Empty(numbered.References);
    }

    [Fact]
    public void Citations_in_code_stay_literal_and_prose_ones_are_numbered()
    {
        var markdown = "Write `[S1]` like this [S2].\n\n```\n[S1] in a fence\n```\n\nThen [S1].";
        var numbered = CitationNumberer.Number([new("A", markdown)], [S(1), S(2)], CitationStyles.Get("apa"), Accessed);

        Assert.Equal("Write `[S1]` like this [1].\n\n```\n[S1] in a fence\n```\n\nThen [2].", numbered.Sections[0].Markdown);
        Assert.Equal([new SourceId(2), new SourceId(1)], numbered.References.Select(r => r.Source));
    }

    [Fact]
    public void A_source_cited_only_in_code_gets_no_reference()
    {
        var numbered = CitationNumberer.Number([new("A", "Only `[S1]` in code.")], [S(1)], CitationStyles.Get("apa"), Accessed);

        Assert.Equal("Only `[S1]` in code.", numbered.Sections[0].Markdown);
        Assert.Empty(numbered.References);
    }
}
