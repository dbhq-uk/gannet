namespace Gannet.Core.Domain;

/// <summary>
/// A source found during a run. Holds metadata only: never page text or snippets, which stay in memory
/// for one run (some providers' terms forbid storing them).
/// </summary>
public sealed record Source(SourceId Id, Uri Url, string Title, string Service, string Query, int Round)
{
    public SourceScore? Score { get; init; }

    /// <summary>True once the credibility score is at or above the run's minimum.</summary>
    public bool Included { get; init; }

    /// <summary>True once page text was obtained in this run (by a reader or with the search result).</summary>
    public bool PageRead { get; init; }

    /// <summary>Which reader or search service supplied the page text.</summary>
    public string? Reader { get; init; }

    public bool Extracted { get; init; }
}

public sealed record SourceScore(int Value, CredibilityLevel Level, IReadOnlyList<string> Rules);

/// <summary>A short pointer to a source for events and lists.</summary>
public sealed record SourceRef(SourceId Id, string Title, Uri Url);
