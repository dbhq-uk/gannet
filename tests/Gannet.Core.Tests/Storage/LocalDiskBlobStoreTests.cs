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
    [InlineData("runs/x.json\n")]
    [InlineData("runs/a.tmp-1/x.json")]
    [InlineData("runs/x.json.tmp-abc")]
    [InlineData("runs/con")]
    [InlineData("runs/CON.txt")]
    [InlineData("runs/con.json")]
    [InlineData("runs/nul/x.json")]
    [InlineData("runs/aux.x")]
    [InlineData("runs/prn")]
    [InlineData("runs/com1")]
    [InlineData("runs/com9.json")]
    [InlineData("runs/lpt1")]
    [InlineData("runs/lpt9.log")]
    [InlineData("runs/x.")]
    [InlineData("runs/a./x.json")]
    public async Task Rejects_unsafe_keys(string key)
    {
        var store = new LocalDiskBlobStore(root);
        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, new byte[] { 1 }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(key, CancellationToken.None));
        if (key.Length > 0)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ListAsync(key, CancellationToken.None));
        }
    }

    [Theory]
    [InlineData("runs/console/x.json")]
    [InlineData("runs/r_abc/run.json")]
    [InlineData("runs/communist.json")]
    [InlineData("runs/com10.json")]
    public async Task Accepts_ordinary_keys_that_resemble_reserved_names(string key)
    {
        var store = new LocalDiskBlobStore(root);
        await store.PutAsync(key, new byte[] { 1 }, CancellationToken.None);
        Assert.NotNull(await store.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task A_symlinked_directory_is_refused()
    {
        var outside = Path.Combine(Path.GetTempPath(), "gannet-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateDirectory(root);
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "runs"), outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // creating symlinks is not permitted here
            }

            var store = new LocalDiskBlobStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync("runs/r_abc/run.json", new byte[] { 1 }, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("runs/r_abc/run.json", CancellationToken.None));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task A_symlinked_file_is_refused()
    {
        var outsideFile = Path.Combine(Path.GetTempPath(), "gannet-outside-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(outsideFile, "secret");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "runs", "r_abc"));
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "runs", "r_abc", "run.json"), outsideFile);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // creating symlinks is not permitted here
            }

            var store = new LocalDiskBlobStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("runs/r_abc/run.json", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync("runs/r_abc/run.json", new byte[] { 1 }, CancellationToken.None));
            Assert.Equal("secret", await File.ReadAllTextAsync(outsideFile));
        }
        finally
        {
            File.Delete(outsideFile);
        }
    }

    [Fact]
    public async Task Get_of_a_missing_key_or_folder_is_null()
    {
        var store = new LocalDiskBlobStore(root);
        Assert.Null(await store.GetAsync("runs/r_abc/run.json", CancellationToken.None));
        Directory.CreateDirectory(root);
        Assert.Null(await store.GetAsync("runs/r_abc/run.json", CancellationToken.None));
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
