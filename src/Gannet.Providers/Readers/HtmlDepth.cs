namespace Gannet.Providers.Readers;

/// <summary>
/// A linear pre-scan that finds the one thing that makes HTML parsing slow: deep nesting of unclosed elements (20,000 nested divs, 100 KB, took
/// 17 seconds in AngleSharp). It does not count tags. A real page has tens of thousands of tags, but they are closed and shallow.
/// </summary>
internal static class HtmlDepth
{
    public const int MaxDepth = 256;

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "basefont", "bgsound", "br", "col", "command", "embed", "frame", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr",
    };

    // The parser closes these by itself when the next one starts, so unclosed ones do not nest.
    private static readonly HashSet<string> EndTagOptional = new(StringComparer.Ordinal)
    {
        "p", "li", "dt", "dd", "tr", "td", "th", "thead", "tbody", "tfoot", "caption", "colgroup", "option", "optgroup", "rt", "rp",
    };

    // Their content is text, not elements.
    private static readonly HashSet<string> RawText = new(StringComparer.Ordinal) { "script", "style", "textarea", "title" };

    /// <summary>True when the open-element depth ever goes above <paramref name="maxDepth"/>. One pass over the text, with no backtracking.</summary>
    public static bool Exceeds(string html, int maxDepth = MaxDepth)
    {
        // Stack of open names, with a count per name so an end tag with no matching start is ignored in O(1), as the parser ignores it.
        var stack = new List<string>();
        var open = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0 || lt + 1 >= html.Length)
            {
                break;
            }

            var next = html[lt + 1];
            if (next == '!')
            {
                if (string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0)
                {
                    var close = html.IndexOf("-->", lt + 2, StringComparison.Ordinal);
                    i = close < 0 ? html.Length : close + 3;
                }
                else
                {
                    i = SkipTo(html, lt + 2);
                }

                continue;
            }

            if (next == '?')
            {
                i = SkipTo(html, lt + 2);
                continue;
            }

            var isEnd = next == '/';
            var nameStart = isEnd ? lt + 2 : lt + 1;
            if (nameStart >= html.Length || !char.IsAsciiLetter(html[nameStart]))
            {
                i = isEnd ? SkipTo(html, nameStart) : lt + 1;
                continue;
            }

            var nameEnd = nameStart;
            while (nameEnd < html.Length && !char.IsWhiteSpace(html[nameEnd]) && html[nameEnd] != '/' && html[nameEnd] != '>')
            {
                nameEnd++;
            }

            var name = html[nameStart..nameEnd].ToLowerInvariant();
            var tagEnd = TagEnd(html, nameEnd, out var selfClosing);
            i = tagEnd;

            if (isEnd)
            {
                if (open.GetValueOrDefault(name) > 0)
                {
                    string popped;
                    do
                    {
                        popped = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        open[popped]--;
                    }
                    while (popped != name);
                }

                continue;
            }

            if (RawText.Contains(name))
            {
                var close = html.IndexOf("</" + name, tagEnd, StringComparison.OrdinalIgnoreCase);
                i = close < 0 ? html.Length : close;
                continue;
            }

            if (Void.Contains(name) || EndTagOptional.Contains(name))
            {
                continue;
            }

            if (selfClosing && (open.GetValueOrDefault("svg") > 0 || open.GetValueOrDefault("math") > 0))
            {
                continue;
            }

            stack.Add(name);
            open[name] = open.GetValueOrDefault(name) + 1;
            if (stack.Count > maxDepth)
            {
                return true;
            }
        }

        return false;
    }

    private static int SkipTo(string html, int from)
    {
        var gt = html.IndexOf('>', from);
        return gt < 0 ? html.Length : gt + 1;
    }

    /// <summary>The index after the closing '>' of a tag, skipping quoted attribute values (which may contain '>').</summary>
    private static int TagEnd(string html, int from, out bool selfClosing)
    {
        selfClosing = false;
        var afterEquals = false;
        for (var k = from; k < html.Length; k++)
        {
            var c = html[k];
            if (c == '>')
            {
                selfClosing = k > from && html[k - 1] == '/';
                return k + 1;
            }

            if (afterEquals && (c == '"' || c == '\''))
            {
                var close = html.IndexOf(c, k + 1);
                if (close < 0)
                {
                    return html.Length;
                }

                k = close;
                afterEquals = false;
                continue;
            }

            if (c == '=')
            {
                afterEquals = true;
            }
            else if (!char.IsWhiteSpace(c))
            {
                afterEquals = false;
            }
        }

        return html.Length;
    }
}
