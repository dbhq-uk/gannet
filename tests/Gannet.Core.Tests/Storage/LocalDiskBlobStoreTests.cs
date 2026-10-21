using System.Text;
using Gannet.Core.Storage;

namespace Gannet.Core.Tests.Storage;

public sealed class LocalDiskBlobStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gannet-blob-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Put_get_list_and_delete()
    {
        var store = new LocalDiskBlobStore(root);
        await store.PutAsync("runs/r_abc/run.json", Encoding.UTF8.GetBytes("{}"), CancellationToken.None);
        await store.PutAsync("runs/r_abc/events.jsonl", Encoding.UTF8.GetBytes("x"), CancellationToken.None);

        Assert.Equal("{}", Encoding.UTF8.GetString((await store.GetAsync("runs/r_abc/run.json", CancellationToken.None))!));
        Assert.Equal(["runs/r_abc/events.jsonl", "runs/r_abc/run.json"], await store.ListAsync("runs/r_abc/", CancellationToken.None));

        await store.DeleteAsync("runs/r_abc/run.json", CancellationToken.None);
        Assert.Null(await store.GetAsync("runs/r_abc/run.json", CancellationToken.None));
        await store.DeleteAsync("runs/r_abc/missing.json", CancellationToken.None); // no throw
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("/abs.json")]
    [InlineData("runs/../../x")]
    [InlineData("runs\\x")]
    [InlineData("runs/X.json")]
    [InlineData("")]
    public async Task Rejects_unsafe_keys(string key)
    {
        var store = new LocalDiskBlobStore(root);
        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, new byte[] { 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_put_leaves_no_temp_file()
    {
        var store = new LocalDiskBlobStore(root);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PutAsync("runs/r_abc/run.json", new byte[] { 1 }, cts.Token));

        var leftovers = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => p.Contains(".tmp-", StringComparison.Ordinal))
            : [];
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task A_failed_move_leaves_no_temp_file()
    {
        var store = new LocalDiskBlobStore(root);
        // A directory sits where the file should go, so the final move fails after the temp file is written.
        Directory.CreateDirectory(Path.Combine(root, "runs", "r_abc", "run.json"));

        await Assert.ThrowsAnyAsync<Exception>(() => store.PutAsync("runs/r_abc/run.json", new byte[] { 1 }, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
    }
}
