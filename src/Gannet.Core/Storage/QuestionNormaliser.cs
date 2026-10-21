using System.Text.RegularExpressions;

namespace Gannet.Core.Storage;

/// <summary>Lower case, single spaces, no trailing punctuation: "  What is X?? " and "what is x" match.</summary>
public static partial class QuestionNormaliser
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalise(string question) =>
        Whitespace().Replace(question.Trim().ToLowerInvariant(), " ").TrimEnd('?', '.', '!', ' ');
}
