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

    public static Report Assemble(string question, ResearchPlan plan, IReadOnlyList<ReportSection> checkedSections, IReadOnlyList<Source> sources, ICitationStyle style, DateOnly accessed)
    {
        var cleaned = checkedSections.Select(s => s with { Markdown = Clean(s.Title, s.Markdown) }).ToList();
        var coverage = Coverage(cleaned.Select(s => s.Markdown));
        var numbered = CitationNumberer.Number(cleaned, sources, style, accessed);

        var md = new StringBuilder();
        md.Append("# ").Append(question.Trim()).Append("\n\n");
        md.Append(CultureInfo.InvariantCulture, $"This report draws on {numbered.References.Count} sources across {numbered.Sections.Count} sections.\n\n");
        md.Append("## Research objectives\n\n");
        for (var i = 0; i < plan.Objectives.Count; i++)
        {
            md.Append(CultureInfo.InvariantCulture, $"{i + 1}. {plan.Objectives[i]}\n");
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
    public static double Coverage(IEnumerable<string> checkedMarkdown)
    {
        int paragraphs = 0, covered = 0;
        foreach (var markdown in checkedMarkdown)
        {
            foreach (var block in BlankLine().Split(MaskCode(markdown.ReplaceLineEndings("\n"))))
            {
                var trimmed = block.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
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
        if (firstLine.StartsWith('#') && string.Equals(firstLine.TrimStart('#').Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            text = firstLineEnd < 0 ? string.Empty : text[(firstLineEnd + 1)..].Trim();
        }

        return TopHeading().Replace(text, "### ");
    }
}
