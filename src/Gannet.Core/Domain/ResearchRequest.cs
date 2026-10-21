namespace Gannet.Core.Domain;

/// <summary>Everything one run needs to know, after defaults are applied and names are checked.</summary>
public sealed record ResearchRequest
{
    public required string Question { get; init; }

    /// <summary>Who started the run: an email, an API key name, or "local".</summary>
    public required string Owner { get; init; }

    /// <summary>Configured model name for planning, the search agent, the gap check and writing.</summary>
    public required string MainModel { get; init; }

    /// <summary>Configured model name for per-source extraction. Null means use the main model.</summary>
    public string? FastModel { get; init; }

    /// <summary>Configured search service name.</summary>
    public required string Search { get; init; }

    /// <summary>Configured reader names, in the order to try them.</summary>
    public IReadOnlyList<string> Readers { get; init; } = ["builtin"];

    public ResearchBudget Budget { get; init; } = new();

    public string CitationStyle { get; init; } = "apa";
}

/// <summary>Limits on one run. Searches are per round; pages are per run.</summary>
public sealed record ResearchBudget
{
    public int MaxSearches { get; init; } = 6;
    public int ResultsPerSearch { get; init; } = 5;
    public int MaxPages { get; init; } = 12;
    public int MaxRounds { get; init; } = 1;
    public int MaxSections { get; init; } = 8;
    public int MinCredibility { get; init; } = 40;

    public ResearchBudget Validated() => new()
    {
        MaxSearches = Math.Clamp(MaxSearches, 1, 30),
        ResultsPerSearch = Math.Clamp(ResultsPerSearch, 1, 10),
        MaxPages = Math.Clamp(MaxPages, 0, 40),
        MaxRounds = Math.Clamp(MaxRounds, 0, 3),
        MaxSections = Math.Clamp(MaxSections, 1, 12),
        MinCredibility = Math.Clamp(MinCredibility, 0, 100),
    };
}
