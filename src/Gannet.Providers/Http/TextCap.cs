namespace Gannet.Providers.Http;

internal static class TextCap
{
    /// <summary>
    /// Cuts to at most maxCharacters (a negative cap counts as 0). Prefers a paragraph break, then a space, but only
    /// when that keeps at least half of the cap; otherwise it cuts hard. Never ends on half a surrogate pair.
    /// </summary>
    public static string Cut(string text, int maxCharacters)
    {
        maxCharacters = Math.Max(0, maxCharacters);
        if (text.Length <= maxCharacters)
        {
            return text;
        }

        var head = text[..maxCharacters];
        var minimum = maxCharacters / 2;

        var paragraph = Math.Max(
            head.LastIndexOf("\n\n", StringComparison.Ordinal),
            head.LastIndexOf("\r\n\r\n", StringComparison.Ordinal));
        if (paragraph > 0)
        {
            var cut = head[..paragraph].TrimEnd();
            if (cut.Length > 0 && cut.Length >= minimum)
            {
                return cut;
            }
        }

        var space = head.LastIndexOf(' ');
        if (space > 0)
        {
            var cut = head[..space].TrimEnd();
            if (cut.Length > 0 && cut.Length >= minimum)
            {
                return cut;
            }
        }

        // The text goes on past the cap, so a high surrogate at the end has lost its pair.
        return head.Length > 0 && char.IsHighSurrogate(head[^1]) ? head[..^1] : head;
    }
}
