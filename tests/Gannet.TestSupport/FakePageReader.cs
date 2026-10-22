using Gannet.Core.Services;

namespace Gannet.TestSupport;

public sealed class FakePageReader(string name, Func<Uri, PageContent> read) : IPageReader
{
    private int calls;

    public int Calls => calls;

    public string Name => name;

    public string Type => name;

    public string DisplayName => name;

    public ServiceTerms Terms => new(true, "test");

    public Task<PageContent> ReadAsync(Uri url, int maxCharacters, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref calls);
        var page = read(url);
        return Task.FromResult(page with { Markdown = page.Markdown.Length > maxCharacters ? page.Markdown[..maxCharacters] : page.Markdown });
    }
}
