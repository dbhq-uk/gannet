using System.Text;
using System.Text.Json;
using Gannet.Core.Domain;
using Gannet.Core.Events;
using Gannet.Core.Json;
using Gannet.Core.Storage;
using Gannet.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Gannet.Core.Tests.Storage;

public class RunStoreTests
{
    private readonly InMemoryBlobStore blobs = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    private RunStore Store() => new(blobs, time);

    private static RunRecord Run(string owner, string question, RunStatus status, DateTimeOffset created) => new()
    {
        Id = RunIds.New(),
        Owner = owner,
        Question = question,
        Request = new ResearchRequest { Question = question, Owner = owner, MainModel = "m", Search = "s" },
        Status = status,
        CreatedAt = created,
    };

    private static void CollectPropertyNames(JsonElement element, HashSet<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    names.Add(property.Name);
                    CollectPropertyNames(property.Value, names);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPropertyNames(item, names);
                }

                break;
            default:
                break;
        }
    }

    [Fact]
    public async Task Save_get_and_list_newest_first_for_one_owner()
    {
        var store = Store();
        var older = Run("dan@dbhq.uk", "First?", RunStatus.Completed, time.GetUtcNow().AddHours(-2));
        var newer = Run("dan@dbhq.uk", "Second?", RunStatus.Running, time.GetUtcNow());
        var other = Run("someone@example.com", "Theirs?", RunStatus.Completed, time.GetUtcNow());
        foreach (var r in new[] { older, newer, other })
        {
            await store.SaveAsync(r, CancellationToken.None);
        }

        Assert.Equal("Second?", (await store.GetAsync(newer.Id, CancellationToken.None))!.Question);
        Assert.Equal([newer.Id, older.Id], (await store.ListAsync("dan@dbhq.uk", CancellationToken.None)).Select(s => s.Id));
        Assert.Equal(3, (await store.ListAsync(null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Invalid_id_returns_null_without_reading()
    {
        Assert.Null(await Store().GetAsync("../runs/index", CancellationToken.None));
        Assert.Equal(0, blobs.Reads);
    }

    [Fact]
    public async Task Events_are_saved_without_transient_ones_and_read_back()
    {
        var store = Store();
        var run = Run("local", "Q?", RunStatus.Running, time.GetUtcNow());
        await store.SaveAsync(run, CancellationToken.None);
        RunEvent[] events = [new SectionStarted(0, 1, "Intro"), new SectionDelta(0, "partial"), new SectionWritten(0, "Intro", 7)];

        await store.SaveEventsAsync(run.Id, events, CancellationToken.None);
        var back = await store.GetEventsAsync(run.Id, CancellationToken.None);

        Assert.Equal(2, back.Count);
        Assert.IsType<SectionStarted>(back[0]);
        Assert.IsType<SectionWritten>(back[1]);
    }

    [Fact]
    public async Task A_bad_line_in_the_event_log_is_skipped()
    {
        var store = Store();
        var run = Run("local", "Q?", RunStatus.Running, time.GetUtcNow());
        var good = JsonSerializer.Serialize<RunEvent>(new SectionWritten(0, "Intro", 7), GannetJson.Options);
        var text = good + "\n" + "{\"type\":\"from-a-newer-version\",\"at\":\"2026-10-21T09:00:00Z\"}\n" + "not json at all\n" + good[..(good.Length / 2)];
        await blobs.PutAsync($"runs/{run.Id}/events.jsonl", Encoding.UTF8.GetBytes(text), CancellationToken.None);

        var back = await store.GetEventsAsync(run.Id, CancellationToken.None);

        var only = Assert.Single(back);
        Assert.IsType<SectionWritten>(only);
    }

    [Fact]
    public async Task Delete_removes_the_run_its_events_and_its_checkpoints()
    {
        var store = Store();
        var run = Run("local", "Q?", RunStatus.Completed, time.GetUtcNow());
        await store.SaveAsync(run, CancellationToken.None);
        await store.SaveEventsAsync(run.Id, [new RunFinished(RunStatus.Completed, null)], CancellationToken.None);
        await blobs.PutAsync($"checkpoints/{run.Id}/index.json", new byte[] { 1 }, CancellationToken.None);

        await store.DeleteAsync(run.Id, CancellationToken.None);

        Assert.Null(await store.GetAsync(run.Id, CancellationToken.None));
        Assert.Empty(await blobs.ListAsync($"runs/{run.Id}/", CancellationToken.None));
        Assert.Empty(await blobs.ListAsync($"checkpoints/{run.Id}/", CancellationToken.None));
        Assert.Empty(await store.ListAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task FindPrevious_matches_normalised_question()
    {
        var store = Store();
        var done = Run("dan@dbhq.uk", "What is X?", RunStatus.Completed, time.GetUtcNow().AddDays(-2));
        var failed = Run("dan@dbhq.uk", "what is x", RunStatus.Failed, time.GetUtcNow().AddDays(-1));
        var theirs = Run("other@example.com", "What is X?", RunStatus.Completed, time.GetUtcNow());
        foreach (var r in new[] { done, failed, theirs })
        {
            await store.SaveAsync(r, CancellationToken.None);
        }

        var found = await store.FindPreviousAsync("dan@dbhq.uk", "  what IS   x ", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(done.Id, found.Id);
        Assert.Null(await store.FindPreviousAsync("dan@dbhq.uk", "What is Y?", CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_saves_all_land_in_the_index()
    {
        var store = Store();
        var runs = Enumerable.Range(0, 20).Select(i => Run("local", $"Q{i}?", RunStatus.Completed, time.GetUtcNow().AddMinutes(i))).ToList();

        await Task.WhenAll(runs.Select(r => store.SaveAsync(r, CancellationToken.None)));

        var listed = await store.ListAsync(null, CancellationToken.None);
        Assert.Equal(20, listed.Count);
        Assert.Equal(runs.Select(r => r.Id).Order(StringComparer.Ordinal), listed.Select(s => s.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_saved_run_holds_no_page_text_properties()
    {
        var store = Store();
        var source = new Source(new SourceId(1), new Uri("https://example.com/a"), "A page", "search", "q", 0)
        {
            Score = new SourceScore(70, CredibilityLevel.High, ["rule"]),
            Included = true,
            PageRead = true,
            Reader = "builtin",
        };
        var run = Run("local", "Q?", RunStatus.Completed, time.GetUtcNow()) with { Sources = [source], ReportMarkdown = "# Report" };

        await store.SaveAsync(run, CancellationToken.None);

        var json = Encoding.UTF8.GetString((await blobs.GetAsync($"runs/{run.Id}/run.json", CancellationToken.None))!);
        using var document = JsonDocument.Parse(json);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectPropertyNames(document.RootElement, names);
        Assert.Contains("sources", names);
        Assert.DoesNotContain("text", names);
        Assert.DoesNotContain("snippet", names);
        Assert.DoesNotContain("content", names);
        Assert.DoesNotContain("pageText", names);
        Assert.DoesNotContain("body", names);
        Assert.DoesNotContain("excerpt", names);
        Assert.DoesNotContain("html", names);
        Assert.DoesNotContain("markdownPage", names);
    }

    [Fact]
    public async Task A_corrupt_index_is_rebuilt_from_the_run_records()
    {
        var store = Store();
        var kept = Run("dan@dbhq.uk", "Kept?", RunStatus.Completed, time.GetUtcNow());
        await store.SaveAsync(kept, CancellationToken.None);
        await blobs.PutAsync("runs/index.json", Encoding.UTF8.GetBytes("{ not json"), CancellationToken.None);

        var listed = await store.ListAsync("dan@dbhq.uk", CancellationToken.None);

        Assert.Equal([kept.Id], listed.Select(s => s.Id));
    }

    [Fact]
    public async Task A_corrupt_index_does_not_stop_save_or_find_previous()
    {
        var store = Store();
        var first = Run("dan@dbhq.uk", "What is X?", RunStatus.Completed, time.GetUtcNow().AddHours(-1));
        await store.SaveAsync(first, CancellationToken.None);
        await blobs.PutAsync("runs/index.json", Encoding.UTF8.GetBytes("[1, 2"), CancellationToken.None);

        var found = await store.FindPreviousAsync("dan@dbhq.uk", "what is x", CancellationToken.None);
        Assert.Equal(first.Id, found?.Id);

        await blobs.PutAsync("runs/index.json", Encoding.UTF8.GetBytes("garbage"), CancellationToken.None);
        var second = Run("dan@dbhq.uk", "Next?", RunStatus.Running, time.GetUtcNow());
        await store.SaveAsync(second, CancellationToken.None);

        Assert.Equal([second.Id, first.Id], (await store.ListAsync(null, CancellationToken.None)).Select(s => s.Id));
    }

    [Fact]
    public async Task Rebuild_picks_up_orphan_runs_and_skips_unreadable_ones()
    {
        var store = Store();
        var indexed = Run("local", "Indexed?", RunStatus.Completed, time.GetUtcNow());
        var orphan = Run("local", "Orphan?", RunStatus.Completed, time.GetUtcNow().AddMinutes(1));
        await store.SaveAsync(indexed, CancellationToken.None);
        await blobs.PutAsync($"runs/{orphan.Id}/run.json", JsonSerializer.SerializeToUtf8Bytes(orphan, GannetJson.Options), CancellationToken.None);
        await blobs.PutAsync($"runs/{RunIds.New()}/run.json", Encoding.UTF8.GetBytes("{ broken"), CancellationToken.None);
        await blobs.PutAsync($"runs/{RunIds.New()}/events.jsonl", Encoding.UTF8.GetBytes("x"), CancellationToken.None);
        Assert.Equal([indexed.Id], (await store.ListAsync(null, CancellationToken.None)).Select(s => s.Id));

        await store.RebuildIndexAsync(CancellationToken.None);

        Assert.Equal([orphan.Id, indexed.Id], (await store.ListAsync(null, CancellationToken.None)).Select(s => s.Id));
    }

    [Fact]
    public async Task A_missing_index_is_rebuilt_from_the_run_records()
    {
        var store = Store();
        var first = Run("local", "One?", RunStatus.Completed, time.GetUtcNow());
        var second = Run("local", "Two?", RunStatus.Completed, time.GetUtcNow().AddMinutes(1));
        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);
        await blobs.DeleteAsync("runs/index.json", CancellationToken.None);

        var listed = await store.ListAsync(null, CancellationToken.None);

        Assert.Equal([second.Id, first.Id], listed.Select(s => s.Id));
        Assert.NotNull(await blobs.GetAsync("runs/index.json", CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_store_is_scanned_once_and_then_has_an_index()
    {
        var counting = new CountingBlobStore(blobs);
        var store = new RunStore(counting, time);

        Assert.Empty(await store.ListAsync(null, CancellationToken.None));
        Assert.Equal(1, counting.Scans);
        Assert.Empty(await store.ListAsync(null, CancellationToken.None));
        Assert.Equal(1, counting.Scans);
    }

    [Fact]
    public async Task Rebuild_skips_a_record_that_cannot_be_read_safely()
    {
        var root = Path.Combine(Path.GetTempPath(), "gannet-runs-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "gannet-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RunStore(new LocalDiskBlobStore(root), time);
            var good = Run("local", "Good?", RunStatus.Completed, time.GetUtcNow());
            await store.SaveAsync(good, CancellationToken.None);
            var linked = RunIds.New();
            Directory.CreateDirectory(Path.Combine(root, "runs", linked));
            await File.WriteAllTextAsync(outside, "{}");
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "runs", linked, "run.json"), outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // creating symlinks is not permitted here
            }

            await store.RebuildIndexAsync(CancellationToken.None);

            Assert.Equal([good.Id], (await store.ListAsync(null, CancellationToken.None)).Select(s => s.Id));
        }
        finally
        {
            File.Delete(outside);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task An_index_of_the_wrong_shape_is_rebuilt()
    {
        var store = Store();
        var run = Run("local", "Q?", RunStatus.Completed, time.GetUtcNow());
        await store.SaveAsync(run, CancellationToken.None);
        await blobs.PutAsync("runs/index.json", Encoding.UTF8.GetBytes("{\"a\":1}"), CancellationToken.None);

        Assert.Equal([run.Id], (await store.ListAsync(null, CancellationToken.None)).Select(s => s.Id));
    }

    [Fact]
    public async Task Concurrent_saves_of_one_run_leave_the_index_and_run_record_in_step()
    {
        var store = new RunStore(new YieldingBlobStore(blobs), time);
        for (var i = 0; i < 60; i++)
        {
            var id = RunIds.New();
            var running = Run("local", "Q?", RunStatus.Running, time.GetUtcNow()) with { Id = id };
            var done = running with { Status = RunStatus.Completed };

            await Task.WhenAll(store.SaveAsync(running, CancellationToken.None), store.SaveAsync(done, CancellationToken.None));

            var saved = await store.GetAsync(id, CancellationToken.None);
            var indexed = (await store.ListAsync(null, CancellationToken.None)).Single(s => s.Id == id);
            Assert.Equal(saved!.Status, indexed.Status);
        }
    }

    [Fact]
    public async Task FindPrevious_normalises_the_stored_question_at_lookup()
    {
        var store = Store();
        var run = Run("dan@dbhq.uk", "What is X?", RunStatus.Completed, time.GetUtcNow());
        await store.SaveAsync(run, CancellationToken.None);
        var stale = new RunSummary(run.Id, run.Owner, run.Question, "an-older-normalisation", run.Status, run.CreatedAt, null);
        await blobs.PutAsync("runs/index.json", JsonSerializer.SerializeToUtf8Bytes(new[] { stale }, GannetJson.Options), CancellationToken.None);

        var found = await store.FindPreviousAsync("dan@dbhq.uk", "what is x", CancellationToken.None);

        Assert.Equal(run.Id, found?.Id);
    }

    [Fact]
    public async Task Owners_match_without_regard_to_case()
    {
        var store = Store();
        var run = Run("Dan@DBHQ.uk", "What is X?", RunStatus.Completed, time.GetUtcNow());
        await store.SaveAsync(run, CancellationToken.None);

        Assert.Single(await store.ListAsync("dan@dbhq.uk", CancellationToken.None));
        Assert.Equal(run.Id, (await store.FindPreviousAsync("DAN@dbhq.uk", "what is x", CancellationToken.None))?.Id);
        Assert.Empty(await store.ListAsync("other@dbhq.uk", CancellationToken.None));
    }

    [Theory]
    [InlineData("r_aaaaaaaaaaaa\n")]
    [InlineData("r_aaaaaaaaaaaaa")]
    [InlineData("")]
    public async Task Malformed_ids_are_invalid_and_never_read(string id)
    {
        Assert.False(RunIds.IsValid(id));
        Assert.Null(await Store().GetAsync(id, CancellationToken.None));
        Assert.Equal(0, blobs.Reads);
    }

    [Fact]
    public async Task Every_concrete_event_type_round_trips_and_transient_ones_are_not_saved()
    {
        var store = Store();
        var run = Run("local", "Q?", RunStatus.Running, time.GetUtcNow());
        var id = new SourceId(1);
        var url = new Uri("https://example.com/a");
        var query = new PlannedQuery("q", "why");
        RunEvent[] events =
        [
            new RunStarted(ResearchMode.Agent, "m", "f", "s", ["builtin"], false),
            new PlanReady(new ResearchPlan("t", ["o"], [query], ["i"])),
            new SearchStarted(0, "q", "r"),
            new SearchResults(0, "q", [new SourceRef(id, "T", url)]),
            new SearchFailed(0, "q", "svc", "failed", "m"),
            new PageReadStarted(id, url),
            new PageRead(id, "builtin", 10),
            new PageReadFailed(id, "builtin", "failed", "m"),
            new SourceScored(id, 50, CredibilityLevel.Medium, true, ["rule"]),
            new FindingsExtracted(id, ["f"]),
            new GapChecked(0, ["g"], [query], true),
            new SectionStarted(0, 1, "T"),
            new SectionDelta(0, "partial"),
            new SectionWritten(0, "T", 5),
            new CitationRemoved("T", "S9", "unknown"),
            new ModelUsage("write", "m", 1, 2),
            new ReportReady(1.0, 1),
            new RunFinished(RunStatus.Completed, null),
            new ChatEntry("e1", ChatLane.Plan, "c", "C", ChatEntryKind.Reply, "redacted"),
            new ChatEntryLive("e1", ChatLane.Plan, "c", "C", ChatEntryKind.Reply, "full"),
        ];
        var concrete = typeof(RunEvent).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(RunEvent)) && !t.IsAbstract).ToHashSet();
        Assert.Equal(concrete, events.Select(e => e.GetType()).ToHashSet());

        await store.SaveEventsAsync(run.Id, events, CancellationToken.None);
        var back = await store.GetEventsAsync(run.Id, CancellationToken.None);

        var expected = events.Where(e => e is not ITransientEvent).Select(e => e.GetType()).ToList();
        Assert.Equal(expected, back.Select(e => e.GetType()).ToList());
        Assert.DoesNotContain(back, e => e is ITransientEvent);
    }

    [Theory]
    [InlineData("  What is X?  ", "what is x")]
    [InlineData("WHAT\tis\n\nx!!", "what is x")]
    [InlineData("Café prices?", "café prices")]
    public void Normaliser_lowers_collapses_and_trims_end_punctuation(string input, string expected)
    {
        Assert.Equal(expected, QuestionNormaliser.Normalise(input));
    }

    [Fact]
    public void Run_ids_are_valid_and_unique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => RunIds.New()).ToList();
        Assert.All(ids, id => Assert.True(RunIds.IsValid(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.False(RunIds.IsValid("r_ABC"));
        Assert.False(RunIds.IsValid("../x"));
    }

    /// <summary>Delays every read and write, before and after it lands, by a random moment, so concurrent saves interleave as they would over a network.</summary>
    private sealed class YieldingBlobStore(IBlobStore inner) : IBlobStore
    {
        public async Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct)
        {
            await Task.Delay(Random.Shared.Next(0, 3), ct);
            await inner.PutAsync(key, content, ct);
            await Task.Delay(Random.Shared.Next(0, 3), ct);
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
        {
            await Task.Delay(Random.Shared.Next(0, 3), ct);
            return await inner.GetAsync(key, ct);
        }

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct) => inner.ListAsync(prefix, ct);

        public Task DeleteAsync(string key, CancellationToken ct) => inner.DeleteAsync(key, ct);
    }

    private sealed class CountingBlobStore(IBlobStore inner) : IBlobStore
    {
        private int scans;

        public int Scans => Volatile.Read(ref scans);

        public Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct) => inner.PutAsync(key, content, ct);

        public Task<byte[]?> GetAsync(string key, CancellationToken ct) => inner.GetAsync(key, ct);

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
        {
            if (prefix == "runs/")
            {
                Interlocked.Increment(ref scans);
            }

            return inner.ListAsync(prefix, ct);
        }

        public Task DeleteAsync(string key, CancellationToken ct) => inner.DeleteAsync(key, ct);
    }
}
