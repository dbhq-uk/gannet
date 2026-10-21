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

        var bytes = JsonSerializer.SerializeToUtf8Bytes(run, GannetJson.Options);

        // The record and its index entry change under one lock, so two saves of one run cannot leave them disagreeing.
        await UpdateIndexAsync(
            async index =>
            {
                await blobs.PutAsync(RunKey(run.Id), bytes, ct);
                return index.Where(s => !SameId(s.Id, run.Id)).Append(RunSummary.From(run));
            },
            ct);
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
        return [.. index.Where(s => owner is null || SameOwner(s.Owner, owner)).OrderByDescending(s => s.CreatedAt)];
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

        await UpdateIndexAsync(
            async index =>
            {
                foreach (var key in (await blobs.ListAsync($"runs/{id}/", ct)).Concat(await blobs.ListAsync($"checkpoints/{id}/", ct)))
                {
                    await blobs.DeleteAsync(key, ct);
                }

                return index.Where(s => !SameId(s.Id, id));
            },
            ct);
    }

    public async Task<RunSummary?> FindPreviousAsync(string owner, string question, CancellationToken ct)
    {
        var normalised = QuestionNormaliser.Normalise(question);
        var index = await ReadIndexAsync(ct);
        return index
            .Where(s => SameOwner(s.Owner, owner) && s.Status == RunStatus.Completed && QuestionNormaliser.Normalise(s.Question) == normalised)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private static string RunKey(string id) => $"runs/{id}/run.json";

    private static string EventsKey(string id) => $"runs/{id}/events.jsonl";

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static bool SameOwner(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rebuilds the index from the run records under runs/. Records that cannot be read are skipped.</summary>
    public async Task RebuildIndexAsync(CancellationToken ct)
    {
        await indexLock.WaitAsync(ct);
        try
        {
            await WriteIndexAsync(await ScanRunsAsync(ct), ct);
        }
        finally
        {
            indexLock.Release();
        }
    }

    private async Task<List<RunSummary>> ReadIndexAsync(CancellationToken ct)
    {
        if (await TryReadIndexAsync(ct) is { } index)
        {
            return index;
        }

        // The index is damaged: rebuild it once, under the lock, and read what the rebuild wrote.
        await indexLock.WaitAsync(ct);
        try
        {
            return await TryReadIndexAsync(ct) ?? await RebuildLockedAsync(ct);
        }
        finally
        {
            indexLock.Release();
        }
    }

    /// <summary>Null when the stored index cannot be parsed. Call with the lock held to act on that.</summary>
    private async Task<List<RunSummary>?> TryReadIndexAsync(CancellationToken ct)
    {
        var bytes = await blobs.GetAsync(IndexKey, ct);
        if (bytes is null)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RunSummary>>(bytes, GannetJson.Options)?.Where(s => s is not null).ToList() ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<RunSummary>> RebuildLockedAsync(CancellationToken ct)
    {
        var rebuilt = await ScanRunsAsync(ct);
        await WriteIndexAsync(rebuilt, ct);
        return rebuilt;
    }

    private async Task<List<RunSummary>> ScanRunsAsync(CancellationToken ct)
    {
        var summaries = new List<RunSummary>();
        foreach (var key in await blobs.ListAsync("runs/", ct))
        {
            var parts = key.Split('/');
            if (parts.Length != 3 || parts[2] != "run.json" || !RunIds.IsValid(parts[1]) || await blobs.GetAsync(key, ct) is not { } bytes)
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<RunRecord>(bytes, GannetJson.Options) is { } run && SameId(run.Id, parts[1]))
                {
                    summaries.Add(RunSummary.From(run));
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // An unreadable record is left out of the index rather than failing the rebuild.
            }
        }

        return summaries;
    }

    private Task WriteIndexAsync(List<RunSummary> index, CancellationToken ct) =>
        blobs.PutAsync(IndexKey, JsonSerializer.SerializeToUtf8Bytes(index, GannetJson.Options), ct);

    private async Task UpdateIndexAsync(Func<List<RunSummary>, Task<IEnumerable<RunSummary>>> change, CancellationToken ct)
    {
        await indexLock.WaitAsync(ct);
        try
        {
            var current = await TryReadIndexAsync(ct) ?? await RebuildLockedAsync(ct);
            await WriteIndexAsync((await change(current)).ToList(), ct);
        }
        finally
        {
            indexLock.Release();
        }
    }
}
