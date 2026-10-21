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
}
