namespace Gannet.Core.Domain;

/// <summary>One point a source makes, in the model's own words, tied to that source.</summary>
public sealed record Finding(SourceId Source, string Text);
