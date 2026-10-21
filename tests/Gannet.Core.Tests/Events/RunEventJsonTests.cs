using System.Text.Json;
using System.Text.Json.Serialization;
using Gannet.Core.Domain;
using Gannet.Core.Events;
using Gannet.Core.Json;

namespace Gannet.Core.Tests.Events;

public class RunEventJsonTests
{
    [Fact]
    public void Events_round_trip_through_json_with_their_type()
    {
        var at = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        RunEvent[] events =
        [
            new PlanReady(new ResearchPlan("T", ["O1"], [new PlannedQuery("q", "why")], ["Intro"])) { At = at },
            new SearchResults(0, "q", [new SourceRef(new SourceId(1), "Title", new Uri("https://example.com/"))]) { At = at },
            new SourceScored(new SourceId(1), 85, CredibilityLevel.High, true, ["Trusted domain: bbc.co.uk"]) { At = at },
            new RunFinished(RunStatus.Completed, null) { At = at },
        ];

        var json = JsonSerializer.Serialize(events, GannetJson.Options);
        Assert.Contains("\"type\":\"plan-ready\"", json, StringComparison.Ordinal);
        Assert.Contains("\"level\":\"high\"", json, StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<RunEvent[]>(json, GannetJson.Options)!;
        Assert.IsType<PlanReady>(back[0]);
        var results = Assert.IsType<SearchResults>(back[1]);
        Assert.Equal(new SourceId(1), results.Sources[0].Id);
        Assert.Equal(at, back[3].At);
    }

    [Fact]
    public void Section_delta_is_transient_and_section_written_is_not()
    {
        Assert.IsAssignableFrom<ITransientEvent>(new SectionDelta(0, "text"));
        RunEvent written = new SectionWritten(0, "Intro", 10);
        Assert.False(written is ITransientEvent);
    }

    [Fact]
    public void Live_chat_entries_are_transient_and_saved_ones_are_not()
    {
        Assert.IsAssignableFrom<ITransientEvent>(new ChatEntryLive("read-S1#1", ChatLane.Read, "read-S1", "S1 Diving", ChatEntryKind.Input, "page text"));
        RunEvent saved = new ChatEntry("read-S1#1", ChatLane.Read, "read-S1", "S1 Diving", ChatEntryKind.Input, "(page text not kept)");
        Assert.False(saved is ITransientEvent);

        var json = JsonSerializer.Serialize<RunEvent>(new ChatEntry("search-0#2", ChatLane.Search, "search-0", "Search", ChatEntryKind.ToolCall, "search(\"q\")"), GannetJson.Options);
        Assert.Contains("\"type\":\"chat-entry\"", json, StringComparison.Ordinal);
        Assert.Contains("\"lane\":\"search\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"tool-call\"", json, StringComparison.Ordinal);
    }

    // Saved event logs are read by their type value, so these names are a persisted format and must never change.
    [Fact]
    public void Event_type_names_are_pinned()
    {
        var names = typeof(RunEvent)
            .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
            .Cast<JsonDerivedTypeAttribute>()
            .Select(a => (string)a.TypeDiscriminator!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "chat-entry",
            "chat-entry-live",
            "citation-removed",
            "findings-extracted",
            "gap-checked",
            "model-usage",
            "page-read",
            "page-read-failed",
            "page-read-started",
            "plan-ready",
            "report-ready",
            "run-finished",
            "run-started",
            "search-failed",
            "search-results",
            "search-started",
            "section-delta",
            "section-started",
            "section-written",
            "source-scored",
        ];
        Assert.Equal(expected, names);
    }
}
