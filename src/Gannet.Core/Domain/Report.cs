namespace Gannet.Core.Domain;

public sealed record ReportSection(string Title, string Markdown);

public sealed record Reference(int Number, SourceId Source, Uri Url, string Title, string Formatted);

public sealed record Report(string Markdown, IReadOnlyList<Reference> References, double CitationCoverage, int SectionCount);
