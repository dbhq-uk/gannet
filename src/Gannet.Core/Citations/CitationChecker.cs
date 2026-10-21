using System.Text;
using Gannet.Core.Domain;

namespace Gannet.Core.Citations;

public sealed record RemovedCitation(string Text, string Reason);

public sealed record CitationCheckResult(string Markdown, IReadOnlyList<RemovedCitation> Removed, IReadOnlyList<SourceId> Cited);

/// <summary>
/// Makes sure every citation in model-written Markdown points at a source the writer was given.
/// Only [S&lt;n&gt;] for a known source survives; anything else that looks like a citation is removed and reported.
/// Code (fenced blocks and inline spans) is copied through untouched.
/// </summary>
public static class CitationChecker
{
    // The longest bracketed group looked at. A citation group is short; this also bounds the work per bracket.
    private const int MaxGroupLength = 400;

    // The longest link target removed after a citation-shaped link text.
    private const int MaxLinkTargetLength = 1000;

    // The longest line prefix of markers looked at, and the most digits in a list number (CommonMark allows nine).
    private const int MaxLinePrefix = 256;
    private const int MaxListNumberDigits = 9;

    // The most characters of a removed citation kept in the report.
    private const int MaxReportedLength = 200;

    // The deepest bracket nesting allowed inside a link target.
    private const int MaxLinkNesting = 4;

    public static CitationCheckResult Check(string markdown, IReadOnlySet<SourceId> known)
    {
        var sweep = new Sweep(markdown, known);
        var last = 0;
        foreach (var code in MarkdownCode.Find(markdown))
        {
            sweep.CheckProse(last, code.Start);
            sweep.Copy(code.Start, code.End);
            last = code.End;
        }

        sweep.CheckProse(last, markdown.Length);
        return new CitationCheckResult(sweep.Output.ToString(), sweep.Removed, sweep.Cited);
    }

    private sealed class Sweep(string markdown, IReadOnlySet<SourceId> known)
    {
        private readonly HashSet<SourceId> _seen = [];

        public StringBuilder Output { get; } = new(markdown.Length);

        public List<RemovedCitation> Removed { get; } = [];

        public List<SourceId> Cited { get; } = [];

        public void Copy(int start, int end) => Output.Append(markdown, start, end - start);

        public void CheckProse(int start, int end)
        {
            var copyFrom = start;
            var i = start;
            while (i < end)
            {
                if (!TryGetKind(markdown[i], out var kind))
                {
                    i++;
                    continue;
                }

                var close = FindClose(i, end, kind);
                if (close < 0)
                {
                    i++;
                    continue;
                }

                // A backslash before the closing bracket is an escape ("\]"); it is not part of the content.
                var contentEnd = markdown[close - 1] == '\\' && close - 1 > i ? close - 1 : close;
                var tokens = CitationGroups.Parse(markdown.AsSpan(i + 1, contentEnd - i - 1), kind);
                if (tokens is null)
                {
                    i++;
                    continue;
                }

                // An escaped opening bracket ("\[") is part of the group as well.
                var groupStart = IsEscaped(i, start) ? i - 1 : i;
                var groupEnd = close + 1;

                if (kind == GroupKind.Square && groupEnd < end && markdown[groupEnd] == ':' && StartsLine(groupStart, copyFrom))
                {
                    // "[S1]: https://..." at the start of a line (behind any list or quote markers) would become a
                    // link reference definition once numbered. Remove the group, its colon and the address a
                    // definition would take; the rest of the line stays as text.
                    Removed.Add(Report(markdown[groupStart..(groupEnd + 1)], "A citation must not define a link"));
                    Copy(copyFrom, groupStart);
                    copyFrom = SkipDestination(groupEnd + 1, end);
                }
                else if (kind == GroupKind.Square && groupEnd < end && markdown[groupEnd] == '(' && TryFindLinkEnd(groupEnd, end, out var linkEnd))
                {
                    Removed.Add(Report(markdown[groupStart..linkEnd], "A citation must not be a link; cite sources like [S3]."));
                    groupEnd = linkEnd;
                    copyFrom = Replace(copyFrom, groupStart, groupEnd, end, string.Empty);
                }
                else
                {
                    copyFrom = Replace(copyFrom, groupStart, groupEnd, end, Judge(tokens, kind));
                }

                i = copyFrom;
            }

            Copy(copyFrom, end);
        }

        // True when only spaces, tabs, ">" quote markers and list markers ("-", "*", "+", "1." and "1)") come before
        // "groupStart" on its line, as the text will be written: a citation removed just before it does not count.
        // The scan stops at the first other character, and is capped, so hostile input cannot make it costly.
        private bool StartsLine(int groupStart, int copyFrom)
        {
            var prefix = new char[MaxLinePrefix];
            var length = 0;
            var reachedLineStart = false;
            for (var k = groupStart - 1; k >= copyFrom && !reachedLineStart && length < MaxLinePrefix; k--)
            {
                reachedLineStart = !TryTake(markdown[k], prefix, ref length, out var stop);
                if (stop)
                {
                    return false;
                }
            }

            for (var k = Output.Length - 1; k >= 0 && !reachedLineStart && length < MaxLinePrefix; k--)
            {
                reachedLineStart = !TryTake(Output[k], prefix, ref length, out var stop);
                if (stop)
                {
                    return false;
                }
            }

            // A prefix longer than the cap is only ever markers, so treat it as the start of a line: removing a
            // colon that was harmless costs nothing, and missing a definition does.
            return length >= MaxLinePrefix || IsMarkerPrefix(prefix.AsSpan(0, length));
        }

        // Adds one character, walking backwards. Returns false at a line break (the line start). "stop" is set for
        // a character that cannot be part of a marker prefix.
        private static bool TryTake(char c, char[] prefix, ref int length, out bool stop)
        {
            stop = false;
            if (c == '\n')
            {
                return false;
            }

            if (length >= prefix.Length)
            {
                return true;
            }

            if (c is ' ' or '\t' or '>' or '-' or '*' or '+' or '.' or ')' or '\r' || char.IsAsciiDigit(c))
            {
                prefix[length++] = c;
                return true;
            }

            stop = true;
            return true;
        }

        // The prefix is held in reverse. Reads it forwards: any mix of spaces, tabs, ">" and list markers.
        private static bool IsMarkerPrefix(ReadOnlySpan<char> reversed)
        {
            var i = reversed.Length - 1;
            while (i >= 0)
            {
                var c = reversed[i];
                if (c is ' ' or '\t' or '>' or '-' or '*' or '+' or '\r')
                {
                    i--;
                }
                else if (char.IsAsciiDigit(c))
                {
                    var digits = 0;
                    while (i >= 0 && char.IsAsciiDigit(reversed[i]))
                    {
                        digits++;
                        i--;
                    }

                    if (i < 0 || reversed[i] is not ('.' or ')') || digits > MaxListNumberDigits)
                    {
                        return false;
                    }

                    i--;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        // After "[S1]:" skips spaces and tabs, and then the address a definition would take: the next word when it
        // starts like a web address or "<". Stops at the end of the line.
        private int SkipDestination(int from, int end)
        {
            var i = from;
            while (i < end && markdown[i] is ' ' or '\t')
            {
                i++;
            }

            var rest = markdown.AsSpan(i, end - i);
            if (!(rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("<")))
            {
                return i;
            }

            while (i < end && markdown[i] is not (' ' or '\t' or '\r' or '\n'))
            {
                i++;
            }

            while (i < end && markdown[i] is ' ' or '\t')
            {
                i++;
            }

            return i;
        }

        // The text kept in a removal report: a group can be 400 characters, and a log needs only the start.
        private static RemovedCitation Report(string text, string reason) =>
            new(text.Length <= MaxReportedLength ? text : text[..MaxReportedLength], reason);

        // Copies the text before the group, then the replacement. A replacement of nothing also takes one
        // leading space or tab (or, at the start of a line, one trailing space). Returns where copying resumes.
        private int Replace(int copyFrom, int groupStart, int groupEnd, int end, string replacement)
        {
            if (replacement.Length > 0)
            {
                Copy(copyFrom, groupStart);
                Output.Append(replacement);
                return groupEnd;
            }

            var cut = groupStart;
            if (cut > copyFrom && markdown[cut - 1] is ' ' or '\t')
            {
                cut--;
            }
            else if ((groupStart == 0 || markdown[groupStart - 1] == '\n') && groupEnd < end && markdown[groupEnd] is ' ' or '\t')
            {
                groupEnd++;
            }

            Copy(copyFrom, cut);
            return groupEnd;
        }

        // Keeps the tokens that are a known source in the strict form S<n>, as [S1][S2]; reports the rest.
        private string Judge(IReadOnlyList<GroupToken> tokens, GroupKind kind)
        {
            var kept = new StringBuilder();
            foreach (var token in tokens)
            {
                if (kind == GroupKind.Round)
                {
                    Removed.Add(Report($"({token.Text})", "Citations must use square brackets, like [S3]."));
                }
                else if (!SourceId.TryParse(token.Text, out var id))
                {
                    var reason = token.Kind == TokenKind.Bare
                        ? "Numbered citations are not allowed; cite sources by ID, like [S3]."
                        : "Not a source ID; cite sources like [S3].";
                    Removed.Add(Report($"[{token.Text}]", reason));
                }
                else if (!known.Contains(id))
                {
                    Removed.Add(Report($"[{token.Text}]", $"No source {token.Text} was given to the writer in this run."));
                }
                else
                {
                    kept.Append('[').Append(id).Append(']');
                    if (_seen.Add(id))
                    {
                        Cited.Add(id);
                    }
                }
            }

            return kept.ToString();
        }

        // True when the bracket at "index" is escaped: an odd number of backslashes directly before it.
        private bool IsEscaped(int index, int start)
        {
            var count = 0;
            for (var k = index - 1; k >= start && markdown[k] == '\\'; k--)
            {
                count++;
            }

            return count % 2 == 1;
        }

        // Finds the closing bracket of the group opened at "open", on the same line, without crossing another
        // opening bracket. Returns -1 when there is none.
        private int FindClose(int open, int end, GroupKind kind)
        {
            var limit = Math.Min(end, open + 1 + MaxGroupLength);
            for (var j = open + 1; j < limit; j++)
            {
                var c = markdown[j];
                if (IsCloser(c, kind))
                {
                    return j;
                }

                if (c == '\n' || TryGetKind(c, out _))
                {
                    return -1;
                }
            }

            return -1;
        }

        // The target of an inline link: "(" to its balanced ")" on one line. Nesting deeper than a few levels
        // is not a real link target, and giving up there keeps hostile input from costing much per bracket.
        private bool TryFindLinkEnd(int open, int end, out int linkEnd)
        {
            var depth = 0;
            var limit = Math.Min(end, open + MaxLinkTargetLength);
            for (var j = open; j < limit && markdown[j] != '\n'; j++)
            {
                if (markdown[j] == '\\')
                {
                    j++;
                }
                else if (markdown[j] == '(')
                {
                    depth++;
                    if (depth > MaxLinkNesting)
                    {
                        break;
                    }
                }
                else if (markdown[j] == ')' && --depth == 0)
                {
                    linkEnd = j + 1;
                    return true;
                }
            }

            linkEnd = 0;
            return false;
        }

        private static bool TryGetKind(char c, out GroupKind kind)
        {
            kind = c is '(' or '\uFF08' ? GroupKind.Round : GroupKind.Square;
            return c is '[' or '\uFF3B' or '(' or '\uFF08';
        }

        private static bool IsCloser(char c, GroupKind kind) =>
            kind == GroupKind.Square ? c is ']' or '\uFF3D' : c is ')' or '\uFF09';
    }
}
