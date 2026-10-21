namespace Gannet.Providers.Http;

internal static class TextCap
{
    /// <summary>Cuts to at most maxCharacters, at a paragraph break if one falls in the second half, else at a space.</summary>
    public static string Cut(string text, int maxCharacters)
    {
        if (text.Length <= maxCharacters)
        {
            return text;
        }

        var head = text[..maxCharacters];
        var paragraph = head.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (paragraph >= maxCharacters / 2)
        {
            return head[..paragraph].TrimEnd();
        }

        var space = head.LastIndexOf(' ');
        return space > 0 ? head[..space].TrimEnd() : head;
    }
}
