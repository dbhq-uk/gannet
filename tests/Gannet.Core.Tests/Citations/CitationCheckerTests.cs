using Gannet.Core.Citations;
using Gannet.Core.Domain;

namespace Gannet.Core.Tests.Citations;

public class CitationCheckerTests
{
    private static HashSet<SourceId> Known(params int[] numbers) => [.. numbers.Select(n => new SourceId(n))];

    [Fact]
    public void Keeps_known_citations_and_lists_them_in_order_of_first_use()
    {
        var result = CitationChecker.Check("A [S2]. B [S1][S2].", Known(1, 2));
        Assert.Equal("A [S2]. B [S1][S2].", result.Markdown);
        Assert.Equal([new SourceId(2), new SourceId(1)], result.Cited);
        Assert.Empty(result.Removed);
    }

    [Theory]
    [InlineData("A [S1, S2].")]
    [InlineData("A [S1; S2].")]
    [InlineData("A [S1,S2].")]
    public void Splits_grouped_citations(string input)
    {
        Assert.Equal("A [S1][S2].", CitationChecker.Check(input, Known(1, 2)).Markdown);
    }

    [Fact]
    public void Check_removes_unknown_and_malformed_citations()
    {
        var result = CitationChecker.Check("One [1]. Two (S3). Three [S99]. Four [s3]. Five [S3].", Known(3));
        Assert.Equal("One. Two. Three. Four. Five [S3].", result.Markdown);
        Assert.Equal(4, result.Removed.Count);
        Assert.Contains(result.Removed, r => r.Text == "[1]" && r.Reason.Contains("cite sources by ID", StringComparison.Ordinal));
        Assert.Contains(result.Removed, r => r.Text == "(S3)" && r.Reason.Contains("square brackets", StringComparison.Ordinal));
        Assert.Contains(result.Removed, r => r.Text == "[S99]" && r.Reason.Contains("No source S99", StringComparison.Ordinal));
        Assert.Contains(result.Removed, r => r.Text == "[s3]");
        Assert.Equal([new SourceId(3)], result.Cited);
    }

    [Fact]
    public void Mixed_group_keeps_the_known_part()
    {
        var result = CitationChecker.Check("X [S1, S9].", Known(1));
        Assert.Equal("X [S1].", result.Markdown);
        Assert.Single(result.Removed);
    }

    [Fact]
    public void Leaves_links_task_lists_footnotes_and_code_alone()
    {
        const string text = "See [the guide](https://example.com/1).\n\n- [ ] todo\n- [x] done\n\nNote[^1]. Use `items[0]`.\n\n```\nvalues[1] = 2;\n```\n";
        var result = CitationChecker.Check(text, Known());
        Assert.Equal(text, result.Markdown);
        Assert.Empty(result.Removed);
    }
}
