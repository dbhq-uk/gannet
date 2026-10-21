using System.Text;
using System.Text.RegularExpressions;
using Gannet.Core.Domain;

namespace Gannet.Core.Citations;

public sealed record RemovedCitation(string Text, string Reason);

public sealed record CitationCheckResult(string Markdown, IReadOnlyList<RemovedCitation> Removed, IReadOnlyList<SourceId> Cited);

/// <summary>
/// Makes sure every citation in model-written Markdown points at a source the writer was given.
/// Only [S&lt;n&gt;] for a known source survives; anything else that looks like a citation is removed and reported.
/// </summary>
public static partial class CitationChecker
{
    // Fenced code blocks and inline code are copied through untouched.
    [GeneratedRegex(@"```.*?```|`[^`\n]*`", RegexOptions.Singleline)]
    private static partial Regex CodeSpans();

    // An optional space, then [tokens] where each token is letters-then-digits, not followed by "(" (a link).
    [GeneratedRegex(@"(?<lead>[ \t]?)\[(?<body>\s*[A-Za-z]{0,6}\d+\s*(?:[,;]\s*[A-Za-z]{0,6}\d+\s*)*)\](?!\()")]
    private static partial Regex BracketGroup();

    // An optional space, then (S3) or (S1, S2).
    [GeneratedRegex(@"(?<lead>[ \t]?)\((?<body>\s*S\d+\s*(?:[,;]\s*S\d+\s*)*)\)")]
    private static partial Regex ParenGroup();

    public static CitationCheckResult Check(string markdown, IReadOnlySet<SourceId> known)
    {
        var removed = new List<RemovedCitation>();
        var cited = new List<SourceId>();
        var seen = new HashSet<SourceId>();
        var output = new StringBuilder(markdown.Length);
        var last = 0;

        foreach (Match code in CodeSpans().Matches(markdown))
        {
            output.Append(CheckProse(markdown[last..code.Index], known, removed, cited, seen));
            output.Append(code.Value);
            last = code.Index + code.Length;
        }

        output.Append(CheckProse(markdown[last..], known, removed, cited, seen));
        return new CitationCheckResult(output.ToString(), removed, cited);
    }

    private static string CheckProse(string text, IReadOnlySet<SourceId> known, List<RemovedCitation> removed, List<SourceId> cited, HashSet<SourceId> seen)
    {
        text = ParenGroup().Replace(text, m =>
        {
            removed.Add(new RemovedCitation(m.Value.Trim(), "Citations must use square brackets, like [S3]."));
            return string.Empty;
        });

        return BracketGroup().Replace(text, m =>
        {
            var kept = new StringBuilder();
            var tokens = m.Groups["body"].Value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (!SourceId.TryParse(token, out var id))
                {
                    var reason = token.All(char.IsAsciiDigit)
                        ? "Numbered citations are not allowed; cite sources by ID, like [S3]."
                        : "Not a source ID; cite sources like [S3].";
                    removed.Add(new RemovedCitation($"[{token}]", reason));
                    continue;
                }

                if (!known.Contains(id))
                {
                    removed.Add(new RemovedCitation($"[{token}]", $"No source {token} was given to the writer in this run."));
                    continue;
                }

                kept.Append('[').Append(id).Append(']');
                if (seen.Add(id))
                {
                    cited.Add(id);
                }
            }

            return kept.Length == 0 ? string.Empty : m.Groups["lead"].Value + kept;
        });
    }
}
