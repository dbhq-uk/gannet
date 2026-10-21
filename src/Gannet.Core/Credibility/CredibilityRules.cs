using System.Text.Json;
using Gannet.Core.Json;

namespace Gannet.Core.Credibility;

/// <summary>
/// The scoring rules as data. The defaults ship embedded; a user file can add domains and override numbers.
/// Numbers are nullable so a user file can leave them out.
/// </summary>
public sealed record CredibilityRules
{
    public int? BaseScore { get; init; }
    public int? TrustedBonus { get; init; }
    public int? SuspiciousPenalty { get; init; }
    public int? HttpsBonus { get; init; }
    public int? HttpPenalty { get; init; }
    public int? ComplexDomainLabels { get; init; }
    public int? ComplexDomainPenalty { get; init; }
    public int? AcademicBonus { get; init; }
    public int? HighAt { get; init; }
    public int? MediumAt { get; init; }
    public IReadOnlyList<string> AcademicPaths { get; init; } = [];
    public IReadOnlyList<string> TrustedDomains { get; init; } = [];
    public IReadOnlyList<string> SuspiciousTopLevelDomains { get; init; } = [];
    public IReadOnlyList<string> SuspiciousDomains { get; init; } = [];

    public static CredibilityRules Parse(string json) =>
        JsonSerializer.Deserialize<CredibilityRules>(json, GannetJson.Options)
        ?? throw new InvalidDataException("The credibility rules file is empty.");

    public static CredibilityRules LoadDefault()
    {
        using var stream = typeof(CredibilityRules).Assembly.GetManifestResourceStream("Gannet.Core.credibility-rules.json")
            ?? throw new InvalidOperationException("The built-in credibility rules are missing from the assembly.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Lists are joined; a number in the user's rules replaces the default.</summary>
    public CredibilityRules MergeWith(CredibilityRules user) => new()
    {
        BaseScore = user.BaseScore ?? BaseScore,
        TrustedBonus = user.TrustedBonus ?? TrustedBonus,
        SuspiciousPenalty = user.SuspiciousPenalty ?? SuspiciousPenalty,
        HttpsBonus = user.HttpsBonus ?? HttpsBonus,
        HttpPenalty = user.HttpPenalty ?? HttpPenalty,
        ComplexDomainLabels = user.ComplexDomainLabels ?? ComplexDomainLabels,
        ComplexDomainPenalty = user.ComplexDomainPenalty ?? ComplexDomainPenalty,
        AcademicBonus = user.AcademicBonus ?? AcademicBonus,
        HighAt = user.HighAt ?? HighAt,
        MediumAt = user.MediumAt ?? MediumAt,
        AcademicPaths = [.. (AcademicPaths ?? []).Union(user.AcademicPaths ?? [], StringComparer.OrdinalIgnoreCase)],
        TrustedDomains = [.. (TrustedDomains ?? []).Union(user.TrustedDomains ?? [], StringComparer.OrdinalIgnoreCase)],
        SuspiciousTopLevelDomains = [.. (SuspiciousTopLevelDomains ?? []).Union(user.SuspiciousTopLevelDomains ?? [], StringComparer.OrdinalIgnoreCase)],
        SuspiciousDomains = [.. (SuspiciousDomains ?? []).Union(user.SuspiciousDomains ?? [], StringComparer.OrdinalIgnoreCase)],
    };
}
