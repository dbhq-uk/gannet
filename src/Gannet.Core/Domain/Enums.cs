namespace Gannet.Core.Domain;

/// <summary>Agent mode: the main model drives the search with tools. Fixed mode: the same tools in a set order.</summary>
public enum ResearchMode
{
    Agent,
    Fixed,
}

public enum RunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
    Interrupted,
}

public enum CredibilityLevel
{
    Low,
    Medium,
    High,
}
