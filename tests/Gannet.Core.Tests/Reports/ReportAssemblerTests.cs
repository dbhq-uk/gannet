using Gannet.Core.Citations;
using Gannet.Core.Domain;
using Gannet.Core.Reports;

namespace Gannet.Core.Tests.Reports;

public class ReportAssemblerTests
{
    private static readonly ResearchPlan Plan = new("Topic", ["Find out A", "Find out B"], [new PlannedQuery("q", "p")], ["Background", "Findings"]);
    private static readonly Source[] Sources =
    [
        new(new SourceId(1), new Uri("https://example.com/1"), "One", "test", "q", 0),
        new(new SourceId(2), new Uri("https://example.com/2"), "Two", "test", "q", 0),
    ];

    [Fact]
    public void Builds_title_objectives_sections_and_references()
    {
        ReportSection[] sections = [new("Background", "Alpha [S2]."), new("Findings", "Beta [S1].")];
        var report = ReportAssembler.Assemble("What is X?", Plan, sections, Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));

        var expected = """
            # What is X?

            This report draws on 2 sources across 2 sections.

            ## Research objectives

            1. Find out A
            2. Find out B

            ## Background

            Alpha [1].

            ## Findings

            Beta [2].

            ## References

            1. Two. (n.d.). Retrieved from https://example.com/2
            2. One. (n.d.). Retrieved from https://example.com/1

            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), report.Markdown);
        Assert.Equal(2, report.References.Count);
        Assert.Equal(1.0, report.CitationCoverage);
        Assert.Equal(2, report.SectionCount);
    }

    [Fact]
    public void Coverage_counts_paragraphs_and_list_items_with_a_citation()
    {
        var coverage = ReportAssembler.Coverage(["Para one [S1].\n\nPara two.\n\n- item [S2]\n- item\n\n### Heading\n\n```\ncode\n```"]);
        Assert.Equal(0.5, coverage);
    }

    [Fact]
    public void Coverage_ignores_a_tilde_fenced_block()
    {
        Assert.Equal(1.0, ReportAssembler.Coverage(["Para [S1].\n\n~~~\ncode line\n~~~"]));
    }

    [Fact]
    public void Coverage_ignores_an_unclosed_fenced_block()
    {
        Assert.Equal(1.0, ReportAssembler.Coverage(["Para [S1].\n\n```\ncode line\n\nmore code"]));
    }

    [Fact]
    public void Coverage_does_not_count_a_citation_inside_a_fenced_block()
    {
        Assert.Equal(0.0, ReportAssembler.Coverage(["Para.\n\n```\n[S1]\n```"]));
    }

    [Fact]
    public void Coverage_does_not_count_a_citation_inside_an_inline_code_span()
    {
        Assert.Equal(0.0, ReportAssembler.Coverage(["The marker is `[S1]` here."]));
    }

    [Fact]
    public void Coverage_counts_a_citation_outside_code_in_the_same_paragraph()
    {
        Assert.Equal(1.0, ReportAssembler.Coverage(["The marker `[S1]` is cited [S2]."]));
    }

    [Fact]
    public void Repeated_section_heading_is_removed_and_inner_headings_are_demoted()
    {
        var report = ReportAssembler.Assemble("Q", Plan, [new("Background", "## Background\n\n# Inner\n\nText [S1].")], Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));
        Assert.Contains("## Background\n\n### Inner\n\nText [1].", report.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Background\n\n## Background", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_without_citations_says_so()
    {
        var report = ReportAssembler.Assemble("Q", Plan, [new("Background", "Text.")], Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));
        Assert.Contains("## References\n\nNo sources were cited.", report.Markdown, StringComparison.Ordinal);
        Assert.Equal(0.0, report.CitationCoverage);
    }
}
