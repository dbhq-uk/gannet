namespace Gannet.Providers.Readers;

internal enum HtmlScan
{
    Ok,
    TooDeep,
    TooManyElements,
}

/// <summary>
/// A linear pre-scan that refuses the pages that make HTML parsing slow or crash it. Two things do that: deep nesting of unclosed elements (20,000 nested
/// divs, 100 KB, took 17 seconds in AngleSharp) and a very large number of elements (100,000 unclosed optgroups, 1 MB, overflowed the stack inside SmartReader,
/// and a stack overflow cannot be caught). A real page has tens of thousands of tags at most, closed and shallow.
/// The scan is conservative on purpose: where it cannot be sure what the parser would do, it assumes the deeper outcome. Over-counting only means the next
/// reader takes over; under-counting would let a hostile page through.
/// </summary>
internal static class HtmlDepth
{
    public const int MaxDepth = 256;
    public const int MaxElements = 30_000;

    // Only real void elements are void. "/>" does nothing on any other element, so <div/> is an open <div>.
    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "basefont", "bgsound", "br", "col", "embed", "frame", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr",
    };

    // The parser closes these by itself when the next one starts, so unclosed ones do not nest.
    private static readonly HashSet<string> EndTagOptional = new(StringComparer.Ordinal)
    {
        "p", "li", "dt", "dd", "tr", "td", "th", "thead", "tbody", "tfoot", "caption", "colgroup", "option",
    };

    // Their content is text, not elements (outside svg and math, where they are ordinary elements).
    private static readonly HashSet<string> RawText = new(StringComparer.Ordinal) { "script", "style", "textarea", "title" };

    // The "special" category of the HTML parsing rules, plus the MathML and SVG integration points. An end tag does not reach past one of these.
    private static readonly HashSet<string> Special = new(StringComparer.Ordinal)
    {
        "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote", "body", "br", "button", "caption", "center", "col",
        "colgroup", "dd", "details", "dir", "div", "dl", "dt", "embed", "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset",
        "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup", "hr", "html", "iframe", "img", "input", "keygen", "li", "link", "listing", "main",
        "marquee", "menu", "meta", "nav", "noembed", "noframes", "noscript", "object", "ol", "p", "param", "plaintext", "pre", "script", "search",
        "section", "select", "source", "style", "summary", "table", "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "title", "tr", "track",
        "ul", "wbr", "xmp", "mi", "mo", "mn", "ms", "mtext", "annotation-xml", "foreignobject", "desc",
    };

    /// <summary>True when the open-element depth ever goes above <paramref name="maxDepth"/>.</summary>
    public static bool Exceeds(string html, int maxDepth = MaxDepth) => Scan(html, maxDepth, int.MaxValue) == HtmlScan.TooDeep;

    /// <summary>Depth first (it stops at the first element past the limit), then the count of start tags in the whole text.</summary>
    public static HtmlScan Scan(string html, int maxDepth = MaxDepth, int maxElements = MaxElements)
    {
        if (DepthExceeds(html, maxDepth))
        {
            return HtmlScan.TooDeep;
        }

        return CountStartTags(html) > maxElements ? HtmlScan.TooManyElements : HtmlScan.Ok;
    }

    /// <summary>Every '&lt;' followed by an ASCII letter, wherever it is: in a script, a comment or an attribute too. It does not depend on the scan below.</summary>
    private static int CountStartTags(string html)
    {
        var count = 0;
        var i = 0;
        while ((i = html.IndexOf('<', i)) >= 0 && i + 1 < html.Length)
        {
            if (char.IsAsciiLetter(html[i + 1]))
            {
                count++;
            }

            i++;
        }

        return count;
    }

    private static bool DepthExceeds(string html, int maxDepth)
    {
        // Stack of open names. Per name, the stack positions where it is open, so an end tag finds its match in O(1) and is ignored when none is open.
        // Specials holds the positions of special elements, so "is a special element above the match" is O(1) too.
        var stack = new List<string>();
        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var specials = new List<int>();
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
                i = string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0 ? CommentEnd(html, lt + 4) : SkipTo(html, lt + 2);
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
            i = TagEnd(html, nameEnd);

            if (isEnd)
            {
                CloseElement(name, stack, positions, specials);
                continue;
            }

            var foreign = Open(positions, "svg") || Open(positions, "math");
            if (!foreign && RawText.Contains(name))
            {
                var close = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                i = close < 0 ? html.Length : close;
                continue;
            }

            if (Void.Contains(name) || EndTagOptional.Contains(name))
            {
                continue;
            }

            if (!positions.TryGetValue(name, out var where))
            {
                positions[name] = where = [];
            }

            where.Add(stack.Count);
            if (Special.Contains(name))
            {
                specials.Add(stack.Count);
            }

            stack.Add(name);
            if (stack.Count > maxDepth)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Open(Dictionary<string, List<int>> positions, string name) =>
        positions.TryGetValue(name, out var where) && where.Count > 0;

    /// <summary>
    /// Pops the matching open element and everything above it, but only when no special element is above the match. Otherwise the end tag is ignored,
    /// as the parser ignores it. Ignoring is the safe side: it keeps the depth.
    /// </summary>
    private static void CloseElement(string name, List<string> stack, Dictionary<string, List<int>> positions, List<int> specials)
    {
        if (!positions.TryGetValue(name, out var where) || where.Count == 0)
        {
            return;
        }

        var match = where[^1];
        if (specials.Count > 0 && specials[^1] > match)
        {
            return;
        }

        while (stack.Count > match)
        {
            var top = stack.Count - 1;
            var popped = stack[top];
            stack.RemoveAt(top);
            positions[popped].RemoveAt(positions[popped].Count - 1);
            if (specials.Count > 0 && specials[^1] == top)
            {
                specials.RemoveAt(specials.Count - 1);
            }
        }
    }

    private static int SkipTo(string html, int from)
    {
        var gt = html.IndexOf('>', from);
        return gt < 0 ? html.Length : gt + 1;
    }

    /// <summary>The index after a comment that opened with "&lt;!--" and whose text starts at <paramref name="from"/>: "&gt;", "-&gt;", "--&gt;" or "--!&gt;".</summary>
    private static int CommentEnd(string html, int from)
    {
        if (from < html.Length && html[from] == '>')
        {
            return from + 1;
        }

        if (string.CompareOrdinal(html, from, "->", 0, 2) == 0)
        {
            return from + 2;
        }

        for (var q = from; (q = html.IndexOf("--", q, StringComparison.Ordinal)) >= 0; q++)
        {
            if (q + 2 < html.Length && html[q + 2] == '>')
            {
                return q + 3;
            }

            if (q + 3 < html.Length && html[q + 2] == '!' && html[q + 3] == '>')
            {
                return q + 4;
            }
        }

        return html.Length;
    }

    /// <summary>
    /// The index after the closing '&gt;' of a tag, following the tokenizer: a quote opens a value only straight after "name=" (an '=' with no name before
    /// it is part of a name), an unquoted value runs to white space or '&gt;', and quotes inside a name are just characters.
    /// </summary>
    private static int TagEnd(string html, int from)
    {
        var k = from;
        while (k < html.Length)
        {
            var c = html[k];
            if (c == '>')
            {
                return k + 1;
            }

            if (char.IsWhiteSpace(c) || c == '/')
            {
                k++;
                continue;
            }

            // An attribute name starts here. A leading '=' is part of the name.
            k++;
            while (k < html.Length && !char.IsWhiteSpace(html[k]) && html[k] != '/' && html[k] != '>' && html[k] != '=')
            {
                k++;
            }

            // After the name: white space, then possibly '='.
            var after = k;
            while (after < html.Length && char.IsWhiteSpace(html[after]))
            {
                after++;
            }

            if (after >= html.Length)
            {
                return html.Length;
            }

            if (html[after] != '=')
            {
                k = after;
                continue;
            }

            // A value: skip white space, then a quoted value or an unquoted one.
            after++;
            while (after < html.Length && char.IsWhiteSpace(html[after]))
            {
                after++;
            }

            if (after >= html.Length)
            {
                return html.Length;
            }

            var quote = html[after];
            if (quote is '"' or '\'')
            {
                var close = html.IndexOf(quote, after + 1);
                if (close < 0)
                {
                    return html.Length;
                }

                k = close + 1;
            }
            else
            {
                k = after;
                while (k < html.Length && !char.IsWhiteSpace(html[k]) && html[k] != '>')
                {
                    k++;
                }
            }
        }

        return html.Length;
    }
}
