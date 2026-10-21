using Gannet.Core.Events;

namespace Gannet.Core.Storage;

public interface IRunStore
{
    Task SaveAsync(RunRecord run, CancellationToken ct);

    /// <summary>Null when the ID is malformed or the run does not exist.</summary>
    Task<RunRecord?> GetAsync(string id, CancellationToken ct);

    /// <summary>Newest first. A null owner lists every run (for admins).</summary>
    Task<IReadOnlyList<RunSummary>> ListAsync(string? owner, CancellationToken ct);

    /// <summary>Replaces the saved event log; transient events are left out.</summary>
    Task SaveEventsAsync(string id, IReadOnlyList<RunEvent> events, CancellationToken ct);

    Task<IReadOnlyList<RunEvent>> GetEventsAsync(string id, CancellationToken ct);

    /// <summary>Deletes the run, its event log and its checkpoints.</summary>
    Task DeleteAsync(string id, CancellationToken ct);

    /// <summary>The owner's most recent completed run with the same normalised question, or null.</summary>
    Task<RunSummary?> FindPreviousAsync(string owner, string question, CancellationToken ct);
}
