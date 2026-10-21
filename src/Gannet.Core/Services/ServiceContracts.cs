namespace Gannet.Core.Services;

/// <summary>What a service's terms allow, and the note the UI shows beside it.</summary>
public sealed record ServiceTerms(bool MayPersistResults, string Note);

public sealed record SearchHit(string Title, Uri Url, string Snippet, string? Content = null, DateTimeOffset? Published = null);

/// <summary>A search service. Implementations are plain HTTP clients; they never write results anywhere.</summary>
public interface ISearchProvider
{
    /// <summary>The configured name, e.g. "tavily" or "brave-work".</summary>
    string Name { get; }

    /// <summary>The kind of service, e.g. "tavily".</summary>
    string Type { get; }

    string DisplayName { get; }

    ServiceTerms Terms { get; }

    /// <summary>True when results can carry page text, so no reader is needed for them.</summary>
    bool ReturnsPageContent { get; }

    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct);
}

public sealed record PageContent(Uri Url, string Title, string Markdown, string Reader);

/// <summary>Fetches one page and returns its main text as Markdown.</summary>
public interface IPageReader
{
    string Name { get; }

    string Type { get; }

    string DisplayName { get; }

    ServiceTerms Terms { get; }

    Task<PageContent> ReadAsync(Uri url, int maxCharacters, CancellationToken ct);
}

public sealed record ServiceDescriptor(string Name, string Type, string DisplayName, ServiceTerms Terms, bool ReturnsPageContent);

public sealed record ModelDescriptor(string Name, string Provider, string Model);

public sealed record ServiceCheckResult(bool Ok, string Message);
