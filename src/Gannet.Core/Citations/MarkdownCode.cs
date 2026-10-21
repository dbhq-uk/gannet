namespace Gannet.Core.Citations;

/// <summary>
/// A half-open range of characters in a string. <paramref name="Fenced"/> is true for a fenced code block, false for an inline code span.
/// <paramref name="MissingClose"/> is, for a fenced block the text ended inside, the fence that would close it; otherwise null.
/// </summary>
internal readonly record struct TextRange(int Start, int End, bool Fenced = false, string? MissingClose = null);

/// <summary>
/// Finds the code in Markdown text, following CommonMark: fenced code blocks (backticks or tildes, closed by a
/// fence of the same character and at least the same length, or by the end of the text) and inline code spans
/// (a backtick run closed by the next run of exactly the same length in the same paragraph).
/// An unmatched backtick run is plain text. Everything is found in one pass, so the cost is linear in the text.
/// </summary>
internal static class MarkdownCode
{
    private const int MaxFenceIndent = 3;
    private const int MinFenceLength = 3;

    private readonly record struct Fence(char Marker, int Length);

    private readonly record struct BacktickRun(int Start, int Length, int Paragraph);

    /// <summary>Returns the code ranges in order. They never overlap.</summary>
    public static IReadOnlyList<TextRange> Find(string text)
    {
        var ranges = new List<TextRange>();
        var runs = new List<BacktickRun>();
        var paragraph = 0;
        Fence? open = null;
        var fenceStart = 0;
        var position = 0;

        while (position < text.Length)
        {
            var newline = text.IndexOf('\n', position);
            var lineEnd = newline < 0 ? text.Length : newline;
            var next = newline < 0 ? text.Length : newline + 1;
            var line = text.AsSpan(position, lineEnd - position);

            if (open is { } fence)
            {
                if (IsClosingFence(line, fence))
                {
                    ranges.Add(new TextRange(fenceStart, next, Fenced: true));
                    open = null;
                    paragraph++;
                }
            }
            else if (TryOpenFence(line, out var opened))
            {
                open = opened;
                fenceStart = position;
                paragraph++;
            }
            else if (line.IsWhiteSpace())
            {
                paragraph++;
            }
            else
            {
                AddRuns(line, position, paragraph, runs);
            }

            position = next;
        }

        if (open is { } unclosed)
        {
            ranges.Add(new TextRange(fenceStart, text.Length, Fenced: true, MissingClose: new string(unclosed.Marker, unclosed.Length)));
        }

        AddSpans(runs, ranges);
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        return ranges;
    }

    private static int CountIndent(ReadOnlySpan<char> line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        return indent;
    }

    private static int CountRun(ReadOnlySpan<char> line, int from, char marker)
    {
        var length = 0;
        while (from + length < line.Length && line[from + length] == marker)
        {
            length++;
        }

        return length;
    }

    private static bool TryOpenFence(ReadOnlySpan<char> line, out Fence fence)
    {
        fence = default;
        var indent = CountIndent(line);
        if (indent > MaxFenceIndent || indent == line.Length || line[indent] is not ('`' or '~'))
        {
            return false;
        }

        var marker = line[indent];
        var length = CountRun(line, indent, marker);

        // A backtick fence's info string cannot contain a backtick; otherwise the line is inline code.
        if (length < MinFenceLength || (marker == '`' && line[(indent + length)..].Contains('`')))
        {
            return false;
        }

        fence = new Fence(marker, length);
        return true;
    }

    private static bool IsClosingFence(ReadOnlySpan<char> line, Fence fence)
    {
        var indent = CountIndent(line);
        if (indent > MaxFenceIndent || indent == line.Length || line[indent] != fence.Marker)
        {
            return false;
        }

        var length = CountRun(line, indent, fence.Marker);
        return length >= fence.Length && line[(indent + length)..].IsWhiteSpace();
    }

    private static void AddRuns(ReadOnlySpan<char> line, int lineStart, int paragraph, List<BacktickRun> runs)
    {
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] != '`')
            {
                i++;
                continue;
            }

            var length = CountRun(line, i, '`');
            runs.Add(new BacktickRun(lineStart + i, length, paragraph));
            i += length;
        }
    }

    private static void AddSpans(List<BacktickRun> runs, List<TextRange> ranges)
    {
        var byLength = new Dictionary<int, List<int>>();
        for (var k = 0; k < runs.Count; k++)
        {
            if (!byLength.TryGetValue(runs[k].Length, out var indexes))
            {
                indexes = [];
                byLength[runs[k].Length] = indexes;
            }

            indexes.Add(k);
        }

        var i = 0;
        while (i < runs.Count)
        {
            var run = runs[i];
            var sameLength = byLength[run.Length];
            var after = sameLength.BinarySearch(i) + 1;
            if (after < sameLength.Count && runs[sameLength[after]].Paragraph == run.Paragraph)
            {
                var closer = runs[sameLength[after]];
                ranges.Add(new TextRange(run.Start, closer.Start + closer.Length));
                i = sameLength[after] + 1;
            }
            else
            {
                i++;
            }
        }
    }
}
