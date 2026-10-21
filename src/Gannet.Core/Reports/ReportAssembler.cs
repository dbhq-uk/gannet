using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Gannet.Core.Citations;
using Gannet.Core.Domain;

namespace Gannet.Core.Reports;

/// <summary>Puts checked sections together into the final report. Pure code: no model calls.</summary>
public static partial class ReportAssembler
{
    [GeneratedRegex(@"\[S\d+\]")]
    private static partial Regex SourceCitation();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"^\s*([-*+]|\d+\.)\s+")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^#{1,2}\s", RegexOptions.Multiline)]
    private static partial Regex TopHeading();

    [GeneratedRegex(@"^#{1,6}(\s|$)")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"^ {0,3}([-*_])( *\1){2,} *$")]
    private static partial Regex ThematicBreak();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(^|\s)#+$")]
    private static partial Regex ClosingHashes();

    public static Report Assemble(string question, ResearchPlan plan, IReadOnlyList<ReportSection> checkedSections, IReadOnlyList<Source> sources, ICitationStyle style, DateOnly accessed)
    {
        var cleaned = checkedSections.Select(s => FlattenSection(s)).ToList();
        var coverage = Coverage(cleaned.Select(s => s.Markdown));
        var numbered = CitationNumberer.Number(cleaned, sources, style, accessed);

        var md = new StringBuilder();
        md.Append("# ").Append(HeadingText(question)).Append("\n\n");
        md.Append(CultureInfo.InvariantCulture, $"This report draws on {numbered.References.Count} {Plural(numbered.References.Count, "source")} across {numbered.Sections.Count} {Plural(numbered.Sections.Count, "section")}.\n\n");
        md.Append("## Research objectives\n\n");
        for (var i = 0; i < plan.Objectives.Count; i++)
        {
            md.Append(CultureInfo.InvariantCulture, $"{i + 1}. {OneLine(plan.Objectives[i])}\n");
        }

        md.Append('\n');
        foreach (var section in numbered.Sections)
        {
            md.Append("## ").Append(section.Title).Append("\n\n").Append(section.Markdown.Trim()).Append("\n\n");
        }

        md.Append("## References\n\n");
        if (numbered.References.Count == 0)
        {
            md.Append("No sources were cited.\n");
        }
        else
        {
            foreach (var reference in numbered.References)
            {
                md.Append(CultureInfo.InvariantCulture, $"{reference.Number}. {reference.Formatted}\n");
            }
        }

        return new Report(md.ToString(), numbered.References, coverage, numbered.Sections.Count);
    }

    /// <summary>
    /// The share of paragraphs and list items that cite a source. Code is read the way <see cref="CitationChecker"/>
    /// and <see cref="CitationNumberer"/> read it: a fenced block is not a paragraph, and a citation inside an
    /// inline code span does not count.
    /// </summary>
    private static ReportSection FlattenSection(ReportSection section)
    {
        var title = NoCitations(OneLine(section.Title));
        return new ReportSection(title, Clean(title, section.Markdown));
    }

    /// <summary>Removes anything citation-shaped: a heading cites nothing, and no source is "known" there.</summary>
    private static string NoCitations(string text) => CitationChecker.Check(text, new HashSet<SourceId>()).Markdown;

    private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";

    /// <summary>Collapses every run of whitespace, newlines included, to one space and trims the ends.</summary>
    private static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>One line of heading text. A closing run of # (a space and then hashes, as in CommonMark) is dropped.</summary>
    private static string HeadingText(string text) => ClosingHashes().Replace(OneLine(text), string.Empty).TrimEnd();

    public static double Coverage(IEnumerable<string> checkedMarkdown)
    {
        int paragraphs = 0, covered = 0;
        foreach (var markdown in checkedMarkdown)
        {
            foreach (var block in BlankLine().Split(MaskCode(markdown.ReplaceLineEndings("\n"))))
            {
                var trimmed = block.Trim();
                if (trimmed.Length == 0 || MarkdownHeading().IsMatch(trimmed) || ThematicBreak().IsMatch(trimmed))
                {
                    continue;
                }

                var lines = trimmed.Split('\n');
                var units = lines.All(l => ListItem().IsMatch(l)) ? lines : [trimmed];
                foreach (var unit in units)
                {
                    paragraphs++;
                    if (SourceCitation().IsMatch(unit))
                    {
                        covered++;
                    }
                }
            }
        }

        return paragraphs == 0 ? 0 : (double)covered / paragraphs;
    }

    /// <summary>
    /// A fenced block becomes a blank line, so it ends the paragraph before it and is not one itself.
    /// An inline code span becomes plain letters, so the paragraph stays but nothing inside the span can match.
    /// </summary>
    private static string MaskCode(string markdown)
    {
        var masked = new StringBuilder(markdown.Length);
        var last = 0;
        foreach (var code in MarkdownCode.Find(markdown))
        {
            masked.Append(markdown, last, code.Start - last);
            if (code.Fenced)
            {
                masked.Append("\n\n");
            }
            else
            {
                for (var i = code.Start; i < code.End; i++)
                {
                    masked.Append(markdown[i] == '\n' ? '\n' : 'x');
                }
            }

            last = code.End;
        }

        return masked.Append(markdown, last, markdown.Length - last).ToString();
    }

    /// <summary>Drops a first line that repeats the section title, and demotes # and ## headings to ###.</summary>
    private static string Clean(string title, string markdown)
    {
        var text = markdown.ReplaceLineEndings("\n").Trim();
        var firstLineEnd = text.IndexOf('\n', StringComparison.Ordinal);
        var firstLine = firstLineEnd < 0 ? text : text[..firstLineEnd];
        if (firstLine.StartsWith('#') && string.Equals(NoCitations(firstLine.TrimStart('#').Trim()), title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            text = firstLineEnd < 0 ? string.Empty : text[(firstLineEnd + 1)..].Trim();
        }

        return DemoteOutsideFences(text);
    }

    /// <summary>
    /// Demotes # and ## headings to ### in the prose, and copies fenced code blocks through verbatim. A fence the
    /// section ended inside is closed, so it cannot swallow the next heading and the reference list.
    /// </summary>
    private static string DemoteOutsideFences(string text)
    {
        var result = new StringBuilder(text.Length);
        var last = 0;
        string? missingClose = null;
        foreach (var code in MarkdownCode.Find(text))
        {
            if (!code.Fenced)
            {
                continue;
            }

            result.Append(TopHeading().Replace(text[last..code.Start], "### ")).Append(text, code.Start, code.End - code.Start);
            last = code.End;
            missingClose = code.MissingClose;
        }

        result.Append(TopHeading().Replace(text[last..], "### "));
        if (missingClose is not null)
        {
            if (result.Length > 0 && result[^1] != '\n')
            {
                result.Append('\n');
            }

            result.Append(missingClose);
        }

        return result.ToString();
    }
}
