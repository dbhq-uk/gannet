using System.Collections.Concurrent;
using Gannet.Core.Services;

namespace Gannet.TestSupport;

public sealed class FakeSearchProvider(string name, Func<string, IReadOnlyList<SearchHit>> search, bool returnsPageContent = false) : ISearchProvider
{
    public ConcurrentQueue<string> Queries { get; } = new();

    public string Name => name;

    public string Type => name;

    public string DisplayName => name;

    public ServiceTerms Terms => new(true, "test");

    public bool ReturnsPageContent => returnsPageContent;

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        Queries.Enqueue(query);
        return Task.FromResult<IReadOnlyList<SearchHit>>([.. search(query).Take(maxResults)]);
    }
}
