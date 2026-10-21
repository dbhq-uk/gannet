using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Gannet.Core.Citations;

internal enum GroupKind
{
    /// <summary>A group in square brackets, such as [S3] or [1, 2].</summary>
    Square,

    /// <summary>A group in round brackets, such as (S3) or (Source 3).</summary>
    Round,
}

internal enum TokenKind
{
    /// <summary>S3, s3, Source 3, Source: S3.</summary>
    Source,

    /// <summary>Letters then digits, such as ref2.</summary>
    Other,

    /// <summary>Digits only, such as 3.</summary>
    Bare,
}

internal sealed record GroupToken(string Text, TokenKind Kind);

/// <summary>
/// Decides whether the text inside a pair of brackets is a citation-like group: nothing but citation-like tokens
/// and separators. The text is normalised first (zero-width characters removed, then Unicode NFKC), so
/// full-width letters and digits, and hidden characters, cannot hide a citation.
/// </summary>
internal static partial class CitationGroups
{
    private const int MaxBareDigits = 3;

    // One token: an optional "Source" prefix, then S<digits> (the space is tolerated), or letters then digits,
    // or digits alone. Decimal parts are matched so that "S1.5" is one token and not an S1 followed by junk.
    [GeneratedRegex(@"(?:source\s*(?::\s*)?)?(?:s\s*\d+(?:\.\d+)*|[a-z]{1,6}\d+(?:\.\d+)*|\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    // What may sit between tokens. Empty entries ("[S1,,S2]", "[S1,]") are just extra separators.
    [GeneratedRegex(@"^(?:[\s,;\-\u2013/]|and|to)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Separators();

    // The same, after the last token, where one full stop is also allowed ("[S99.]").
    [GeneratedRegex(@"^(?:[\s,;\-\u2013/]|and|to)*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSeparators();

    /// <summary>Returns the tokens, or null when the content is not a citation-like group.</summary>
    public static IReadOnlyList<GroupToken>? Parse(ReadOnlySpan<char> content, GroupKind kind)
    {
        var text = Ascii.IsValid(content) ? content.Trim() : Normalise(content).AsSpan();
        var tokens = new List<GroupToken>();
        var position = 0;
        foreach (var match in Token().EnumerateMatches(text))
        {
            if (!Separators().IsMatch(text[position..match.Index]))
            {
                return null;
            }

            position = match.Index + match.Length;
            var tokenText = text.Slice(match.Index, match.Length).ToString();
            tokens.Add(new GroupToken(tokenText, Classify(tokenText)));
        }

        if (tokens.Count == 0 || !TrailingSeparators().IsMatch(text[position..]))
        {
            return null;
        }

        // (1), f(2) and "(s 172)" (a UK statute section) are ordinary prose, so round brackets only count when
        // every token is a strict source form: "Source ..." or an upper-case S directly followed by a digit.
        if (kind == GroupKind.Round)
        {
            return tokens.TrueForAll(t => IsStrictSourceForm(t.Text)) ? tokens : null;
        }

        // A group of only digits is a citation when the numbers are short. [2024] UKSC 1 is a case citation.
        if (tokens.TrueForAll(t => t.Kind == TokenKind.Bare) && tokens.Exists(t => t.Text.EnumerateRunes().Count() > MaxBareDigits))
        {
            return null;
        }

        return tokens;
    }

    private static bool IsStrictSourceForm(string token) =>
        token.StartsWith("source", StringComparison.OrdinalIgnoreCase) || (token.Length > 1 && token[0] == 'S' && char.IsAsciiDigit(token[1]));

    private static TokenKind Classify(string token)
    {
        if (token.StartsWith("source", StringComparison.OrdinalIgnoreCase))
        {
            return TokenKind.Source;
        }

        if (char.IsDigit(token[0]))
        {
            return TokenKind.Bare;
        }

        // S then optional spaces then a digit is a source form; any other letters then digits is not.
        var next = 1;
        while (next < token.Length && char.IsWhiteSpace(token[next]))
        {
            next++;
        }

        return (token[0] is 's' or 'S') && char.IsDigit(token[next]) ? TokenKind.Source : TokenKind.Other;
    }

    private static string Normalise(ReadOnlySpan<char> content)
    {
        var builder = new StringBuilder(content.Length);
        foreach (var c in content)
        {
            if (!IsInvisible(c))
            {
                builder.Append(c);
            }
        }

        var stripped = builder.ToString();
        try
        {
            return stripped.Normalize(NormalizationForm.FormKC).Trim();
        }
        catch (ArgumentException)
        {
            // A lone surrogate cannot be normalised. Drop surrogates for this check only.
            return string.Concat(stripped.Where(c => !char.IsSurrogate(c))).Normalize(NormalizationForm.FormKC).Trim();
        }
    }

    // Every Unicode format (Cf) character, plus the combining grapheme joiner, which is invisible too.
    private static bool IsInvisible(char c) =>
        char.GetUnicodeCategory(c) == UnicodeCategory.Format || c == '\u034F';
}
