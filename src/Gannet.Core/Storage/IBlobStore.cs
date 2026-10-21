using System.Text.RegularExpressions;

namespace Gannet.Core.Storage;

/// <summary>Where gannet keeps run records, event logs and checkpoints: local disk, or an S3-compatible store such as R2.</summary>
public interface IBlobStore
{
    Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct);

    Task<byte[]?> GetAsync(string key, CancellationToken ct);

    /// <summary>Keys starting with the prefix, sorted ordinally.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);

    /// <summary>Deletes the key if it exists. Missing keys are not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}

public static partial class BlobKeys
{
    [GeneratedRegex(@"^[a-z0-9_-][a-z0-9._-]*(/[a-z0-9_-][a-z0-9._-]*)*$")]
    private static partial Regex SafeKey();

    /// <summary>Lower-case letters, digits, dot, underscore, hyphen and slash; no empty, "." or ".." segments.</summary>
    public static string Validate(string key)
    {
        if (string.IsNullOrEmpty(key) || !SafeKey().IsMatch(key) || key.Split('/').Any(s => s is "." or ".."))
        {
            throw new ArgumentException($"Unsafe storage key '{key}'.", nameof(key));
        }

        return key;
    }

    /// <summary>A prefix may end with a slash.</summary>
    public static string ValidatePrefix(string prefix) =>
        prefix.Length == 0 ? prefix : Validate(prefix.TrimEnd('/')) + (prefix.EndsWith('/') ? "/" : string.Empty);
}
