using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Gannet.Core.Storage;

/// <summary>Run IDs look like r_7k2m9x4q1p3a: twelve characters from a 32-letter alphabet, random.</summary>
public static partial class RunIds
{
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    [GeneratedRegex("^r_[a-z0-9]{12}$")]
    private static partial Regex Shape();

    public static string New() => "r_" + RandomNumberGenerator.GetString(Alphabet, 12);

    public static bool IsValid(string? id) => id is not null && Shape().IsMatch(id);
}
