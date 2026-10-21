using Gannet.Core.Domain;

namespace Gannet.Core.Storage;

/// <summary>A run as saved. Never holds page text or search snippets.</summary>
public sealed record RunRecord
{
    public required string Id { get; init; }
    public required string Owner { get; init; }
    public required string Question { get; init; }
    public required ResearchRequest Request { get; init; }
    public RunStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public ResearchMode? Mode { get; init; }
    public ResearchPlan? Plan { get; init; }
    public IReadOnlyList<Source> Sources { get; init; } = [];
    public string? ReportMarkdown { get; init; }
    public IReadOnlyList<Reference> References { get; init; } = [];
    public double? CitationCoverage { get; init; }
    public UsageTotals Usage { get; init; } = UsageTotals.Zero;
    public string? Error { get; init; }
}

public sealed record RunSummary(string Id, string Owner, string Question, string NormalisedQuestion, RunStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt)
{
    public static RunSummary From(RunRecord run) =>
        new(run.Id, run.Owner, run.Question, QuestionNormaliser.Normalise(run.Question), run.Status, run.CreatedAt, run.FinishedAt);
}
