namespace Gannet.Core.Services;

/// <summary>What is configured, by name. The UI, API, MCP and CLI list from here; runs resolve names here.</summary>
public interface IServiceCatalog
{
    IReadOnlyList<ServiceDescriptor> SearchServices { get; }

    IReadOnlyList<ServiceDescriptor> Readers { get; }

    IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>Throws <see cref="UnknownServiceException"/> for a name that is not configured.</summary>
    ISearchProvider GetSearch(string name);

    /// <summary>Throws <see cref="UnknownServiceException"/> for a name that is not configured.</summary>
    IPageReader GetReader(string name);

    /// <summary>A live check; kind is "model", "search" or "reader".</summary>
    Task<ServiceCheckResult> CheckAsync(string kind, string name, CancellationToken ct);
}
