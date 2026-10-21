using System.Text;
using System.Text.Json;
using Gannet.Core.Domain;
using Gannet.Core.Events;
using Gannet.Core.Json;

namespace Gannet.Core.Storage;

/// <summary>
/// Saves runs, their event logs and an index of summaries in a blob store. The index lock is per instance,
/// so register this as a singleton: two instances over one store can lose index updates.
/// </summary>
public sealed class RunStore(IBlobStore blobs, TimeProvider time) : IRunStore
{
    private const string IndexKey = "runs/index.json";
    private readonly SemaphoreSlim indexLock = new(1, 1);

    public TimeProvider Time { get; } = time;

    public async Task SaveAsync(RunRecord run, CancellationToken ct)
    {
        if (!RunIds.IsValid(run.Id))
        {
            throw new ArgumentException($"Invalid run ID '{run.Id}'.", nameof(run));
        }

        await blobs.PutAsync(RunKey(run.Id), JsonSerializer.SerializeToUtf8Bytes(run, GannetJson.Options), ct);
        await UpdateIndexAsync(index => index.Where(s => s.Id != run.Id).Append(RunSummary.From(run)), ct);
    }

    public async Task<RunRecord?> GetAsync(string id, CancellationToken ct)
    {
        if (!RunIds.IsValid(id))
        {
            return null;
        }

        var bytes = await blobs.GetAsync(RunKey(id), ct);
        return bytes is null ? null : JsonSerializer.Deserialize<RunRecord>(bytes, GannetJson.Options);
    }

    public async Task<IReadOnlyList<RunSummary>> ListAsync(string? owner, CancellationToken ct)
    {
        var index = await ReadIndexAsync(ct);
        return [.. index.Where(s => owner is null || s.Owner == owner).OrderByDescending(s => s.CreatedAt)];
    }

    public async Task SaveEventsAsync(string id, IReadOnlyList<RunEvent> events, CancellationToken ct)
    {
        if (!RunIds.IsValid(id))
        {
            throw new ArgumentException($"Invalid run ID '{id}'.", nameof(id));
        }

        var text = new StringBuilder();
        foreach (var e in events.Where(e => e is not ITransientEvent))
        {
            text.Append(JsonSerializer.Serialize(e, GannetJson.Options)).Append('\n');
        }

        await blobs.PutAsync(EventsKey(id), Encoding.UTF8.GetBytes(text.ToString()), ct);
    }

    public async Task<IReadOnlyList<RunEvent>> GetEventsAsync(string id, CancellationToken ct)
    {
        if (!RunIds.IsValid(id))
        {
            return [];
        }

        var bytes = await blobs.GetAsync(EventsKey(id), ct);
        if (bytes is null)
        {
            return [];
        }

        var events = new List<RunEvent>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // An event from another version, or a last line cut short by a crash, is skipped rather than fatal.
            try
            {
                if (JsonSerializer.Deserialize<RunEvent>(line, GannetJson.Options) is { } e)
                {
                    events.Add(e);
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
            }
        }

        return events;
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (!RunIds.IsValid(id))
        {
            return;
        }

        foreach (var key in (await blobs.ListAsync($"runs/{id}/", ct)).Concat(await blobs.ListAsync($"checkpoints/{id}/", ct)))
        {
            await blobs.DeleteAsync(key, ct);
        }

        await UpdateIndexAsync(index => index.Where(s => s.Id != id), ct);
    }

    public async Task<RunSummary?> FindPreviousAsync(string owner, string question, CancellationToken ct)
    {
        var normalised = QuestionNormaliser.Normalise(question);
        var index = await ReadIndexAsync(ct);
        return index
            .Where(s => s.Owner == owner && s.Status == RunStatus.Completed && s.NormalisedQuestion == normalised)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private static string RunKey(string id) => $"runs/{id}/run.json";

    private static string EventsKey(string id) => $"runs/{id}/events.jsonl";

    private async Task<List<RunSummary>> ReadIndexAsync(CancellationToken ct)
    {
        var bytes = await blobs.GetAsync(IndexKey, ct);
        return bytes is null ? [] : JsonSerializer.Deserialize<List<RunSummary>>(bytes, GannetJson.Options) ?? [];
    }

    private async Task UpdateIndexAsync(Func<IEnumerable<RunSummary>, IEnumerable<RunSummary>> change, CancellationToken ct)
    {
        await indexLock.WaitAsync(ct);
        try
        {
            var updated = change(await ReadIndexAsync(ct)).ToList();
            await blobs.PutAsync(IndexKey, JsonSerializer.SerializeToUtf8Bytes(updated, GannetJson.Options), ct);
        }
        finally
        {
            indexLock.Release();
        }
    }
}
