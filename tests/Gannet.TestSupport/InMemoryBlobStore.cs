using System.Collections.Concurrent;
using Gannet.Core.Storage;

namespace Gannet.TestSupport;

public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, byte[]> items = new(StringComparer.Ordinal);
    private int reads;

    public int Reads => Volatile.Read(ref reads);

    public Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        items[BlobKeys.Validate(key)] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        Interlocked.Increment(ref reads);
        return Task.FromResult(items.TryGetValue(BlobKeys.Validate(key), out var value) ? value.ToArray() : null);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
    {
        BlobKeys.ValidatePrefix(prefix);
        return Task.FromResult<IReadOnlyList<string>>([.. items.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)]);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        items.TryRemove(BlobKeys.Validate(key), out _);
        return Task.CompletedTask;
    }
}
