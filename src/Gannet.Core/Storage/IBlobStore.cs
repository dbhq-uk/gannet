using System.Text.RegularExpressions;

namespace Gannet.Core.Storage;

/// <summary>Where gannet keeps run records, event logs and checkpoints: local disk, or an S3-compatible store such as R2.</summary>
public interface IBlobStore
{
    /// <summary>Stores the content under the key, replacing any earlier value. The caller must not change <paramref name="content"/> until the returned task completes.</summary>
    Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct);

    Task<byte[]?> GetAsync(string key, CancellationToken ct);

    /// <summary>Keys starting with the prefix, sorted ordinally.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);

    /// <summary>Deletes the key if it exists. Missing keys are not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}

public static partial class BlobKeys
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    [GeneratedRegex(@"\A[a-z0-9_-][a-z0-9._-]*(/[a-z0-9_-][a-z0-9._-]*)*\z")]
    private static partial Regex SafeKey();

    /// <summary>
    /// Lower-case letters, digits, dot, underscore, hyphen and slash. No empty, "." or ".." segments, no segment that
    /// ends in a dot, contains ".tmp-" (the disk store's temporary files) or is a Windows device name such as "con" or "nul.txt".
    /// </summary>
    public static string Validate(string key)
    {
        if (string.IsNullOrEmpty(key) || !SafeKey().IsMatch(key) || key.Split('/').Any(IsUnsafeSegment))
        {
            throw new ArgumentException($"Unsafe storage key '{key}'.", nameof(key));
        }

        return key;
    }

    /// <summary>A prefix follows the key rules and may end with a slash. Use the slash to list a folder.</summary>
    public static string ValidatePrefix(string prefix) =>
        prefix.Length == 0 ? prefix : Validate(prefix.TrimEnd('/')) + (prefix.EndsWith('/') ? "/" : string.Empty);

    private static bool IsUnsafeSegment(string segment) =>
        segment is "." or ".."
        || segment.EndsWith('.')
        || segment.Contains(".tmp-", StringComparison.Ordinal)
        || ReservedNames.Contains(segment.Split('.')[0]);
}
