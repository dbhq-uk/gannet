using System.Diagnostics;
using System.Globalization;
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

    // Finding 1: citation-like text must not pass through unchecked. Known sources are S1 and S2.
    [Theory]
    [InlineData("[S99,]", "X.")]
    [InlineData("[,S99]", "X.")]
    [InlineData("[S99,,S1]", "X [S1].")]
    [InlineData("[S1-S99]", "X [S1].")]
    [InlineData("[S1\u2013S99]", "X [S1].")]
    [InlineData("[S1 and S99]", "X [S1].")]
    [InlineData("[S99 S1]", "X [S1].")]
    [InlineData("[S 99]", "X.")]
    [InlineData("[Source 99]", "X.")]
    [InlineData("[Source: S99]", "X.")]
    [InlineData("[S1\u200B]", "X [S1].")]
    [InlineData("[S\u200B99]", "X.")]
    [InlineData("[\uFF331]", "X [S1].")]
    [InlineData("[S\uFF11]", "X [S1].")]
    [InlineData("[S\u0663]", "X.")]
    [InlineData("\uFF3BS99\uFF3D", "X.")]
    [InlineData("\uFF3BS1\uFF3D", "X [S1].")]
    [InlineData("[S1.5]", "X.")]
    [InlineData("\\[S99\\]", "X.")]
    [InlineData("(Source 99)", "X.")]
    [InlineData("(S99)", "X.")]
    [InlineData("(S1, S2)", "X.")]
    [InlineData("[S99](http://x)", "X.")]
    [InlineData("[S1](http://x)", "X.")]
    [InlineData("[99](http://x/(y))", "X.")]
    [InlineData("[S1234567]", "X.")]
    [InlineData("[S1 ,S2]", "X [S1][S2].")]
    [InlineData("[S1][s2]", "X [S1].")]
    [InlineData("[123]", "X.")]
    [InlineData("[1, 2]", "X.")]
    public void Closing_sweep_catches_every_citation_like_form(string citation, string expected)
    {
        var result = CitationChecker.Check("X " + citation + ".", Known(1, 2));
        Assert.Equal(expected, result.Markdown);
    }

    [Theory]
    [InlineData("In [2024] UKSC 1 the court held X.")]
    [InlineData("See [2024].")]
    [InlineData("Dates [1999, 2000] differ.")]
    [InlineData("The options are (1) tea and (2) coffee, see f(x) and [x].")]
    [InlineData("A [link](https://example.com/S99) and a [^1] note and [ ] box.")]
    public void Leaves_ordinary_brackets_alone(string text)
    {
        var result = CitationChecker.Check(text, Known(1, 2));
        Assert.Equal(text, result.Markdown);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void Reports_each_removed_token_with_a_reason()
    {
        var result = CitationChecker.Check("X [S99,, S1, 7].", Known(1));
        Assert.Equal("X [S1].", result.Markdown);
        Assert.Equal(["[S99]", "[7]"], result.Removed.Select(r => r.Text));
        Assert.All(result.Removed, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason)));
        Assert.Equal([new SourceId(1)], result.Cited);
    }

    [Fact]
    public void Removed_link_is_reported_whole()
    {
        var result = CitationChecker.Check("X [S99](http://x) y.", Known(1));
        Assert.Equal("X y.", result.Markdown);
        Assert.Equal("[S99](http://x)", Assert.Single(result.Removed).Text);
    }

    // Finding 2: code handling follows CommonMark.
    [Fact]
    public void Unclosed_fence_runs_to_the_end()
    {
        const string text = "Before [S99].\n```\ncode [S99]\nmore [S98]";
        Assert.Equal("Before.\n```\ncode [S99]\nmore [S98]", CitationChecker.Check(text, Known()).Markdown);
    }

    [Fact]
    public void Tilde_fence_is_code()
    {
        const string text = "~~~\nx [S99]\n~~~\nAfter [S99].";
        Assert.Equal("~~~\nx [S99]\n~~~\nAfter.", CitationChecker.Check(text, Known()).Markdown);
    }

    [Fact]
    public void Four_backtick_fence_may_contain_a_three_backtick_line()
    {
        const string text = "````\n```\n[S99]\n```\n````\nAfter [S99].";
        Assert.Equal("````\n```\n[S99]\n```\n````\nAfter.", CitationChecker.Check(text, Known()).Markdown);
    }

    [Fact]
    public void A_fence_is_closed_only_by_the_same_character()
    {
        const string text = "```\n~~~\n[S99]\n~~~\n```\nAfter [S99].";
        Assert.Equal("```\n~~~\n[S99]\n~~~\n```\nAfter.", CitationChecker.Check(text, Known()).Markdown);
    }

    [Fact]
    public void Double_backtick_span_may_contain_a_single_backtick()
    {
        const string text = "Use ``a ` [S99]`` then [S99].";
        Assert.Equal("Use ``a ` [S99]`` then.", CitationChecker.Check(text, Known()).Markdown);
    }

    [Fact]
    public void Prose_between_inline_triple_backtick_mentions_is_still_checked()
    {
        const string text = "Mention ``` here.\n\nClaim [S99].\n\nMention ``` again.";
        Assert.Equal("Mention ``` here.\n\nClaim.\n\nMention ``` again.", CitationChecker.Check(text, Known()).Markdown);
    }

    [Fact]
    public void Unmatched_backtick_run_is_literal_text()
    {
        Assert.Equal("Tick ` then.", CitationChecker.Check("Tick ` then [S99].", Known()).Markdown);
    }

    [Fact]
    public void A_four_space_indent_is_not_a_fence()
    {
        Assert.Equal("    ```\nthen.", CitationChecker.Check("    ```\nthen [S99].", Known()).Markdown);
    }

    [Fact]
    public void Handles_crlf_input()
    {
        const string text = "A [S99].\r\n```\r\nx [S99]\r\n```\r\nB [S1].\r\n";
        var result = CitationChecker.Check(text, Known(1));
        Assert.Equal("A.\r\n```\r\nx [S99]\r\n```\r\nB [S1].\r\n", result.Markdown);
        Assert.Equal([new SourceId(1)], result.Cited);
    }

    [Fact]
    public void Citation_at_the_start_of_a_line()
    {
        var result = CitationChecker.Check("[S1] begins.\n[S99] next.\n[S2]", Known(1, 2));
        Assert.Equal("[S1] begins.\nnext.\n[S2]", result.Markdown);
    }

    public static TheoryData<string> HostileUnits => new()
    {
        "[1 ",
        "[1](((((((x",
        "(S1 ",
        "[S1, ",
        "`` ` ``` ` ",
        "[\u200B1 ",
        "[source" + new string(' ', 390) + "x]",
        "(source:" + new string(' ', 390) + "x)",
        "[1" + string.Concat(Enumerable.Repeat(" 1", 190)) + "]",
    };

    // Each shape is 1,000,000 characters. The work is linear, so each takes about a second or less in Release
    // and a few seconds in Debug on a busy machine. The bound is generous on purpose: a timing test must not
    // fail because the machine is loaded, but a quadratic scan would take minutes at this size and still fail.
    [Theory]
    [MemberData(nameof(HostileUnits))]
    public void Hostile_input_finishes_quickly(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, 1_000_000 / unit.Length));
        _ = CitationChecker.Check("[1] warm-up", Known(1));
        var timer = Stopwatch.StartNew();
        _ = CitationChecker.Check(text, Known(1));
        timer.Stop();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), string.Create(CultureInfo.InvariantCulture, $"Took {timer.Elapsed.TotalSeconds:F1}s"));
    }

    // Round brackets: "(s 172)" is a UK statute reference. Only "Source ..." and an upper-case S joined to a digit count.
    [Theory]
    [InlineData("A (s 172) duty.")]
    [InlineData("A (s172) duty.")]
    [InlineData("A (S 172) duty.")]
    [InlineData("A (s 1, s 2) duty.")]
    public void Statute_references_in_round_brackets_survive(string text)
    {
        var result = CitationChecker.Check(text, Known(1, 2));
        Assert.Equal(text, result.Markdown);
        Assert.Empty(result.Removed);
    }

    [Theory]
    [InlineData("(S3)")]
    [InlineData("(S1, S2)")]
    [InlineData("(Source 3)")]
    [InlineData("(source: S3)")]
    public void Round_bracket_source_forms_are_still_removed(string citation)
    {
        Assert.Equal("A.", CitationChecker.Check("A " + citation + ".", Known(3)).Markdown);
    }

    // A citation at the start of a line (behind any list or quote markers) followed by a colon would become a
    // Markdown link reference definition once numbered. Only the group and its colon are removed; the rest stays.
    [Theory]
    [InlineData("[S1]: the regulator said", "the regulator said")]
    [InlineData("   [S1]: the regulator said", "   the regulator said")]
    [InlineData("[S99]: the regulator said", "the regulator said")]
    [InlineData("[S99][S1]: the regulator said", "the regulator said")]
    [InlineData("[S1, S2]:the regulator said", "the regulator said")]
    [InlineData("- [S1]: the regulator said", "- the regulator said")]
    [InlineData("* [S1]: the regulator said", "* the regulator said")]
    [InlineData("+ [S1]: the regulator said", "+ the regulator said")]
    [InlineData("1. [S1]: the regulator said", "1. the regulator said")]
    [InlineData("1) [S1]: the regulator said", "1) the regulator said")]
    [InlineData("> [S1]: the regulator said", "> the regulator said")]
    [InlineData(">[S1]: the regulator said", ">the regulator said")]
    [InlineData("> - [S1]: the regulator said", "> - the regulator said")]
    [InlineData("  - 12. > [S1]: the regulator said", "  - 12. > the regulator said")]
    [InlineData("-\t[S1]: the regulator said", "-\tthe regulator said")]
    public void A_citation_cannot_define_a_link(string line, string expectedLine)
    {
        var result = CitationChecker.Check("Before [S1].\n" + line + "\nAfter [S2].", Known(1, 2));
        Assert.Equal("Before [S1].\n" + expectedLine + "\nAfter [S2].", result.Markdown);
        Assert.Contains(result.Removed, r => r.Reason == "A citation must not define a link" || r.Reason.Contains("No source", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[S1]: https://evil.example/x", "")]
    [InlineData("- [S1]: https://evil.example/x", "- ")]
    [InlineData("1. [S1]:<https://evil.example/x>", "1. ")]
    [InlineData("> - [S1]: https://evil.example/x \"title\"", "> - \"title\"")]
    public void The_address_a_definition_would_have_used_goes_with_the_group(string line, string expectedLine)
    {
        Assert.Equal(expectedLine, CitationChecker.Check(line, Known(1)).Markdown);
    }

    [Fact]
    public void A_removed_link_definition_reports_the_reason_and_only_the_group()
    {
        var result = CitationChecker.Check("[S1]: the regulator said\r\nNext.", Known(1));
        Assert.Equal("the regulator said\r\nNext.", result.Markdown);
        var removed = Assert.Single(result.Removed);
        Assert.Equal("[S1]:", removed.Text);
        Assert.Equal("A citation must not define a link", removed.Reason);
        Assert.Empty(result.Cited);
    }

    [Fact]
    public void Removed_text_is_capped_at_200_characters()
    {
        var result = CitationChecker.Check("X [" + string.Join(", ", Enumerable.Repeat("S99", 60)) + "](http://x) y.", Known(1));
        Assert.All(result.Removed, r => Assert.True(r.Text.Length <= 200));
        Assert.Equal("X y.", result.Markdown);
    }

    [Fact]
    public void A_citation_followed_by_a_colon_after_prose_is_left_to_the_normal_rules()
    {
        Assert.Equal("Note [S1]: see this.", CitationChecker.Check("Note [S1]: see this.", Known(1)).Markdown);

        // Any indent counts: a list item can be nested deeper than four spaces.
        Assert.Equal("    code", CitationChecker.Check("    [S1]: code", Known(1)).Markdown);
        Assert.Equal("Note - [S1]: see this.", CitationChecker.Check("Note - [S1]: see this.", Known(1)).Markdown);
    }

    [Fact]
    public void A_link_definition_inside_a_fence_is_code_and_stays()
    {
        const string text = "```\n[S1]: https://example.com\n```";
        Assert.Equal(text, CitationChecker.Check(text, Known(1)).Markdown);
    }

    [Theory]
    [InlineData("X [S\u20629].")]
    [InlineData("X [S\u034F9].")]
    public void Invisible_format_characters_cannot_hide_a_citation(string text)
    {
        var result = CitationChecker.Check(text, Known(1));
        Assert.Equal("X.", result.Markdown);
        Assert.Single(result.Removed);
    }

    [Fact]
    public void Slash_to_and_a_trailing_full_stop_are_citation_separators()
    {
        Assert.Equal("X [S1][S2].", CitationChecker.Check("X [S1/S2].", Known(1, 2)).Markdown);
        Assert.Equal("X [S1].", CitationChecker.Check("X [S1 to S99].", Known(1)).Markdown);
        Assert.Equal("X.", CitationChecker.Check("X [S99.].", Known(1)).Markdown);
    }
}
