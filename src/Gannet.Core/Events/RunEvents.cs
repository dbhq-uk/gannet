using System.Text.Json.Serialization;
using Gannet.Core.Domain;

namespace Gannet.Core.Events;

/// <summary>Marks events that are streamed live but never saved (section text as it is written).</summary>
public interface ITransientEvent;

/// <summary>
/// Something that happened in a run. The UI timeline, the CLI progress lines, the API's event stream and the
/// saved event log (used to replay a run) are all built from these. No event carries page text or snippets.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RunStarted), "run-started")]
[JsonDerivedType(typeof(PlanReady), "plan-ready")]
[JsonDerivedType(typeof(SearchStarted), "search-started")]
[JsonDerivedType(typeof(SearchResults), "search-results")]
[JsonDerivedType(typeof(SearchFailed), "search-failed")]
[JsonDerivedType(typeof(PageReadStarted), "page-read-started")]
[JsonDerivedType(typeof(PageRead), "page-read")]
[JsonDerivedType(typeof(PageReadFailed), "page-read-failed")]
[JsonDerivedType(typeof(SourceScored), "source-scored")]
[JsonDerivedType(typeof(FindingsExtracted), "findings-extracted")]
[JsonDerivedType(typeof(GapChecked), "gap-checked")]
[JsonDerivedType(typeof(SectionStarted), "section-started")]
[JsonDerivedType(typeof(SectionDelta), "section-delta")]
[JsonDerivedType(typeof(SectionWritten), "section-written")]
[JsonDerivedType(typeof(CitationRemoved), "citation-removed")]
[JsonDerivedType(typeof(ModelUsage), "model-usage")]
[JsonDerivedType(typeof(ReportReady), "report-ready")]
[JsonDerivedType(typeof(RunFinished), "run-finished")]
[JsonDerivedType(typeof(ChatEntry), "chat-entry")]
[JsonDerivedType(typeof(ChatEntryLive), "chat-entry-live")]
public abstract record RunEvent
{
    public DateTimeOffset At { get; init; }
}

public sealed record RunStarted(ResearchMode Mode, string MainModel, string FastModel, string Search, IReadOnlyList<string> Readers, bool Resumed) : RunEvent;

public sealed record PlanReady(ResearchPlan Plan) : RunEvent;

public sealed record SearchStarted(int Round, string Query, string? Reason) : RunEvent;

public sealed record SearchResults(int Round, string Query, IReadOnlyList<SourceRef> Sources) : RunEvent;

/// <summary>Error is one of: rate-limited, challenge, auth-required, quota-exhausted, failed.</summary>
public sealed record SearchFailed(int Round, string Query, string Service, string Error, string Message) : RunEvent;

public sealed record PageReadStarted(SourceId Source, Uri Url) : RunEvent;

public sealed record PageRead(SourceId Source, string Reader, int Characters) : RunEvent;

public sealed record PageReadFailed(SourceId Source, string Reader, string Error, string Message) : RunEvent;

public sealed record SourceScored(SourceId Source, int Score, CredibilityLevel Level, bool Included, IReadOnlyList<string> Rules) : RunEvent;

public sealed record FindingsExtracted(SourceId Source, IReadOnlyList<string> Findings) : RunEvent;

public sealed record GapChecked(int Round, IReadOnlyList<string> Gaps, IReadOnlyList<PlannedQuery> FollowUps, bool SearchingAgain) : RunEvent;

public sealed record SectionStarted(int Index, int Total, string Title) : RunEvent;

public sealed record SectionDelta(int Index, string Text) : RunEvent, ITransientEvent;

public sealed record SectionWritten(int Index, string Title, int Characters) : RunEvent;

public sealed record CitationRemoved(string Section, string Citation, string Reason) : RunEvent;

public sealed record ModelUsage(string Step, string Model, long InputTokens, long OutputTokens) : RunEvent;

public sealed record ReportReady(double CitationCoverage, int References) : RunEvent;

public sealed record RunFinished(RunStatus Status, string? Error) : RunEvent;

/// <summary>The Run page's four chat columns, one per stage. The gap check shows in Plan.</summary>
public enum ChatLane
{
    Plan,
    Search,
    Read,
    Write,
}

public enum ChatEntryKind
{
    Instructions,
    Input,
    Reply,
    ToolCall,
    ToolResult,
}

/// <summary>One message in a stage's chat, as saved. Never holds page text or snippets: where the live message did, this is its redacted copy.</summary>
public sealed record ChatEntry(string EntryId, ChatLane Lane, string Chat, string ChatTitle, ChatEntryKind Kind, string Text) : RunEvent;

/// <summary>The same message with its full text, page text and snippets included. Streamed live, never saved.</summary>
public sealed record ChatEntryLive(string EntryId, ChatLane Lane, string Chat, string ChatTitle, ChatEntryKind Kind, string Text) : RunEvent, ITransientEvent;
