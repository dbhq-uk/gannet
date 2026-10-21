namespace Gannet.Core.Storage;

public sealed class LocalDiskBlobStore(string root) : IBlobStore
{
    private readonly string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temp, content, ct);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        try
        {
            return await File.ReadAllBytesAsync(path, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
    {
        BlobKeys.ValidatePrefix(prefix);
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> keys = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).Contains(".tmp-", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        return Task.FromResult(keys);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>Cleanup must not hide the error that made it necessary.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsLink(string path)
    {
        var info = new FileInfo(path);
        return info.LinkTarget is not null || ((int)info.Attributes != -1 && info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    private string PathFor(string key)
    {
        var segments = BlobKeys.Validate(key).Split('/');
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Storage key '{key}' resolves outside the storage root.", nameof(key));
        }

        // A link anywhere under the root could point outside it, so none is followed.
        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
            {
                throw new ArgumentException($"Storage key '{key}' passes through a link.", nameof(key));
            }
        }

        return path;
    }
}
