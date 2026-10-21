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
    public void Headings_inside_fenced_code_are_left_alone()
    {
        var body = "## Outside\n\n```bash\n# comment\n## two\n```\n\n~~~\n# tilde comment\n## tilde two\n~~~\n\nText [S1].";
        var report = ReportAssembler.Assemble("Q", Plan, [new("Background", body)], Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));
        Assert.Contains("### Outside\n\n```bash\n# comment\n## two\n```\n\n~~~\n# tilde comment\n## tilde two\n~~~\n\nText [1].", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Question_is_flattened_to_one_line_and_loses_closing_hashes()
    {
        var report = ReportAssembler.Assemble("Line one\nline two #", Plan, [new("Background", "Text [S1].")], Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));
        Assert.StartsWith("# Line one line two\n\n", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Question_keeps_a_hash_that_belongs_to_a_word()
    {
        var report = ReportAssembler.Assemble("Is C#", Plan, [new("Background", "Text [S1].")], Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));
        Assert.StartsWith("# Is C#\n\n", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Section_titles_and_objectives_cannot_split_lines_or_inject_headings()
    {
        ResearchPlan plan = new("Topic", ["Find A\n## Injected", "Find  B"], [new PlannedQuery("q", "p")], ["Background"]);
        var report = ReportAssembler.Assemble("Q", plan, [new("Back\n\n## ground", "Text [S1].")], Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));
        Assert.Contains("1. Find A ## Injected\n2. Find B\n", report.Markdown, StringComparison.Ordinal);
        Assert.Contains("\n## Back ## ground\n\nText [1].", report.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\n## Injected", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_counts_a_paragraph_that_starts_with_a_hash_but_is_not_a_heading()
    {
        Assert.Equal(1.0, ReportAssembler.Coverage(["#1 tip [S1]"]));
        Assert.Equal(0.0, ReportAssembler.Coverage(["#1 tip"]));
    }

    [Fact]
    public void Coverage_still_skips_real_headings()
    {
        Assert.Equal(1.0, ReportAssembler.Coverage(["# Title\n\n###### Deep\n\n#\n\nA [S1]"]));
    }

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    [InlineData("_____")]
    [InlineData("  * * *  ")]
    public void Coverage_does_not_count_a_thematic_break(string rule)
    {
        Assert.Equal(1.0, ReportAssembler.Coverage([$"A [S1]\n\n{rule}\n\nB [S1]"]));
    }

    [Fact]
    public void Report_without_citations_says_so()
    {
        var report = ReportAssembler.Assemble("Q", Plan, [new("Background", "Text.")], Sources, CitationStyles.Get("apa"), new DateOnly(2026, 10, 21));
        Assert.Contains("## References\n\nNo sources were cited.", report.Markdown, StringComparison.Ordinal);
        Assert.Equal(0.0, report.CitationCoverage);
    }

    private static readonly DateOnly Accessed = new(2026, 10, 21);

    [Fact]
    public void A_citation_cannot_become_a_link_definition_anywhere_in_the_pipeline()
    {
        var checkedSection = CitationChecker.Check("Real claim [S1].\n\n[S1]: https://attacker.example/x\n\n   [S2]: https://attacker.example/y\n\nEnd.", new HashSet<SourceId> { new(1), new(2) });
        var report = ReportAssembler.Assemble("Q", Plan, [new("Background", checkedSection.Markdown)], Sources, CitationStyles.Get("apa"), Accessed);
        var html = ReportExporters.Get("html").Export(report.Markdown, "Q");

        Assert.DoesNotContain("attacker.example", report.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.example", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"https://attacker", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_source_title_cannot_start_a_link_definition_or_a_heading()
    {
        Source[] sources = [new(new SourceId(1), new Uri("https://example.com/1"), "A\n\n[1]: https://x\n\n# Injected", "test", "q", 0)];
        var report = ReportAssembler.Assemble("Q", Plan, [new("Background", "Text [S1].")], sources, CitationStyles.Get("apa"), Accessed);

        var lines = report.Markdown.Split('\n');
        Assert.DoesNotContain(lines, l => l.StartsWith("[1]:", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith('#') && l.Contains("Injected", StringComparison.Ordinal));
        Assert.Single(report.References);
        Assert.DoesNotContain('\n', report.References[0].Formatted);
    }

    [Fact]
    public void An_unclosed_fence_at_the_end_of_a_section_does_not_swallow_the_rest_of_the_report()
    {
        ReportSection[] sections = [new("Background", "Intro [S1].\n\n```python\nprint(1)"), new("Findings", "Beta [S2].")];
        var report = ReportAssembler.Assemble("Q", Plan, sections, Sources, CitationStyles.Get("apa"), Accessed);
        var html = ReportExporters.Get("html").Export(report.Markdown, "Q");

        Assert.Contains("print(1)\n```\n\n## Findings", report.Markdown, StringComparison.Ordinal);
        Assert.Contains("<h2>Findings</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>References</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code class=\"language-python\">print(1)\n\n## Findings", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("```\ncode")]
    [InlineData("~~~~\ncode\n~~~")]
    [InlineData("````text\ncode\n```")]
    public void Every_kind_of_unclosed_fence_is_closed(string body)
    {
        var report = ReportAssembler.Assemble("Q", Plan, [new("Background", body + "\n\n")], Sources, CitationStyles.Get("apa"), Accessed);
        var html = ReportExporters.Get("html").Export(report.Markdown, "Q");
        Assert.Contains("<h2>References</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code>code\n\n## References", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_title_carries_no_citation()
    {
        var report = ReportAssembler.Assemble("Q", Plan, [new("Duties [S1] and [S99]", "Text [S1].")], Sources, CitationStyles.Get("apa"), Accessed);
        Assert.Contains("\n## Duties and\n\nText [1].", report.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Duties [", report.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_uses_the_singular_for_one()
    {
        var one = ReportAssembler.Assemble("Q", Plan, [new("Background", "Text [S1].")], Sources, CitationStyles.Get("apa"), Accessed);
        Assert.Contains("This report draws on 1 source across 1 section.\n", one.Markdown, StringComparison.Ordinal);

        var many = ReportAssembler.Assemble("Q", Plan, [new("A", "Text [S1]."), new("B", "Text [S2]."), new("C", "Text.")], Sources, CitationStyles.Get("apa"), Accessed);
        Assert.Contains("This report draws on 2 sources across 3 sections.\n", many.Markdown, StringComparison.Ordinal);
    }
}
