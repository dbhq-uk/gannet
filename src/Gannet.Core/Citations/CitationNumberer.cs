using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Gannet.Core.Domain;

namespace Gannet.Core.Citations;

public sealed record NumberedSections(IReadOnlyList<ReportSection> Sections, IReadOnlyList<Reference> References);

/// <summary>
/// Turns checked [S&lt;n&gt;] citations into [1], [2], ... in order of first use, and builds the reference list.
/// Code (fenced blocks and inline spans) is left as written, because <see cref="CitationChecker"/> never checks it.
/// </summary>
public static partial class CitationNumberer
{
    [GeneratedRegex(@"\[S(?<n>\d+)\]")]
    private static partial Regex SourceCitation();

    public static NumberedSections Number(IReadOnlyList<ReportSection> sections, IReadOnlyList<Source> sources, ICitationStyle style, DateOnly accessed)
    {
        var byId = sources.ToDictionary(s => s.Id);
        var numbers = new Dictionary<SourceId, int>();
        var references = new List<Reference>();

        string Replace(Match m)
        {
            var id = new SourceId(int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture));
            if (!byId.TryGetValue(id, out var source))
            {
                return string.Empty; // already removed by CitationChecker; defensive only
            }

            if (!numbers.TryGetValue(id, out var number))
            {
                number = numbers.Count + 1;
                numbers[id] = number;
                references.Add(new Reference(number, id, source.Url, source.Title, style.Format(new ReferenceSource(source.Url, source.Title), accessed)));
            }

            return string.Create(CultureInfo.InvariantCulture, $"[{number}]");
        }

        var renumbered = sections.Select(section => section with { Markdown = NumberProse(section.Markdown, Replace) }).ToList();
        return new NumberedSections(renumbered, references);
    }

    private static string NumberProse(string markdown, MatchEvaluator replace)
    {
        var output = new StringBuilder(markdown.Length);
        var last = 0;
        foreach (var code in MarkdownCode.Find(markdown))
        {
            output.Append(SourceCitation().Replace(markdown[last..code.Start], replace));
            output.Append(markdown, code.Start, code.End - code.Start);
            last = code.End;
        }

        output.Append(SourceCitation().Replace(markdown[last..], replace));
        return output.ToString();
    }
}
