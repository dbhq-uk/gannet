namespace Gannet.Core.Domain;

public sealed record PlannedQuery(string Query, string Purpose);

public sealed record ResearchPlan(
    string Topic,
    IReadOnlyList<string> Objectives,
    IReadOnlyList<PlannedQuery> Queries,
    IReadOnlyList<string> Outline);
