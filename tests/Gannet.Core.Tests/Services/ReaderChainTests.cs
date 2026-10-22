using Gannet.Core.Services;
using Gannet.TestSupport;

namespace Gannet.Core.Tests.Services;

public class ReaderChainTests
{
    private static readonly Uri Url = new("https://site.example/a");

    private sealed class ThrowingReader(string name, Exception error) : IPageReader
    {
        public string Name => name;

        public string Type => name;

        public string DisplayName => name;

        public ServiceTerms Terms => new(true, "test");

        public Task<PageContent> ReadAsync(Uri url, int maxCharacters, CancellationToken ct) => throw error;
    }

    private static readonly FakePageReader Works = new("works", u => new PageContent(u, "T", "text", "works"));

    [Fact]
    public async Task Chain_falls_through_on_challenge_and_reports_reader()
    {
        var blocked = new FakePageReader("builtin", _ => throw new ChallengeDetectedException("Built-in reader", "The site answered with a bot challenge page."));
        var limited = new FakePageReader("jina", _ => throw new RateLimitedException("Jina Reader", TimeSpan.FromSeconds(10), "Jina is limiting the request rate (429)."));
        var works = new FakePageReader("brightdata", u => new PageContent(u, "T", "text", "brightdata"));

        var outcome = await new ReaderChain([blocked, limited, works]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Equal("brightdata", outcome.Page!.Reader);
        Assert.Equal([("builtin", "challenge"), ("jina", "rate-limited")], outcome.Failures.Select(f => (f.Reader, f.Error)));
    }

    [Fact]
    public async Task All_failing_returns_no_page_and_every_failure()
    {
        var empty = new FakePageReader("builtin", u => new PageContent(u, "T", "   ", "builtin"));
        var broken = new FakePageReader("jina", _ => throw new HttpRequestException("connection refused"));

        var outcome = await new ReaderChain([empty, broken]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Null(outcome.Page);
        Assert.Equal(2, outcome.Failures.Count);
        Assert.All(outcome.Failures, f => Assert.Equal("failed", f.Error));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var slow = new FakePageReader("builtin", _ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReaderChain([slow]).ReadAsync(Url, 8000, cts.Token));
    }

    [Fact]
    public async Task Cancellation_thrown_by_a_reader_while_the_caller_is_cancelled_propagates()
    {
        using var cts = new CancellationTokenSource();
        var reader = new ThrowingReader("builtin", new OperationCanceledException());
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReaderChain([reader, Works]).ReadAsync(Url, 8000, cts.Token));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("auth-required")]
    [InlineData("quota-exhausted")]
    public async Task Every_typed_service_error_is_recorded_and_the_chain_moves_on(string kind)
    {
        ServiceException error = kind switch
        {
            "auth-required" => new AuthRequiredException("Svc", "needs a key"),
            "quota-exhausted" => new QuotaExhaustedException("Svc", "limit reached"),
            _ => new ServiceFailedException("Svc", "the reader is busy; try the next reader"),
        };

        var outcome = await new ReaderChain([new ThrowingReader("first", error), Works]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Equal("works", outcome.Page!.Reader);
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal(("first", kind, error.Message), (failure.Reader, failure.Error, failure.Message));
    }

    private sealed class UnknownKindOfException() : Exception("SECRET-PAGE-TEXT from the server");

    [Fact]
    public async Task An_exception_type_the_chain_does_not_know_is_recorded_as_unexpected()
    {
        var outcome = await new ReaderChain([new ThrowingReader("jina", new UnknownKindOfException()), Works]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Equal("works", outcome.Page!.Reader);
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal(("jina", "unexpected", nameof(UnknownKindOfException)), (failure.Reader, failure.Error, failure.Message));
    }

    [Fact]
    public async Task A_timeout_that_is_not_the_callers_cancellation_is_recorded_as_a_failure()
    {
        var reader = new ThrowingReader("builtin", new TaskCanceledException("timed out"));

        var outcome = await new ReaderChain([reader, Works]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Equal("works", outcome.Page!.Reader);
        Assert.Equal(("builtin", "failed", "Timed out."), (outcome.Failures[0].Reader, outcome.Failures[0].Error, outcome.Failures[0].Message));
    }

    [Fact]
    public async Task A_reader_bug_is_recorded_as_unexpected_with_only_the_type_name()
    {
        const string PageText = "SECRET-PAGE-TEXT from https://site.example/a";
        var buggy = new ThrowingReader("builtin", new InvalidOperationException(PageText));

        var outcome = await new ReaderChain([buggy, Works]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Equal("works", outcome.Page!.Reader);
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal(("builtin", "unexpected", nameof(InvalidOperationException)), (failure.Reader, failure.Error, failure.Message));
        Assert.DoesNotContain("SECRET", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_typed_error_wrapped_in_an_http_exception_keeps_its_kind_and_message()
    {
        var typed = new QuotaExhaustedException("Svc", "Svc says the quota or credit is used up (402).");
        var reader = new ThrowingReader("jina", new HttpRequestException("wrapper text SECRET-WRAPPER", typed));

        var outcome = await new ReaderChain([reader, Works]).ReadAsync(Url, 8000, CancellationToken.None);

        var failure = Assert.Single(outcome.Failures);
        Assert.Equal(("jina", "quota-exhausted", typed.Message), (failure.Reader, failure.Error, failure.Message));
    }

    [Fact]
    public async Task An_http_exception_with_some_other_inner_exception_is_still_a_connection_failure()
    {
        var reader = new ThrowingReader("jina", new HttpRequestException("wrapper", new IOException("SECRET-INNER")));

        var outcome = await new ReaderChain([reader]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Equal("failed", outcome.Failures[0].Error);
        Assert.DoesNotContain("SECRET", outcome.Failures[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_messages_never_carry_the_text_of_an_http_exception()
    {
        var reader = new ThrowingReader("jina", new HttpRequestException("body: <html>SECRET-PAGE-TEXT</html>", null, System.Net.HttpStatusCode.BadGateway));

        var outcome = await new ReaderChain([reader]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.DoesNotContain("SECRET", outcome.Failures[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_messages_never_carry_the_text_of_an_unexpected_exception_inside_a_message_chain()
    {
        var nested = new ThrowingReader("builtin", new InvalidDataException("SECRET-PAGE-TEXT", new IOException("SECRET-INNER")));

        var outcome = await new ReaderChain([nested]).ReadAsync(Url, 8000, CancellationToken.None);

        Assert.Equal(nameof(InvalidDataException), outcome.Failures[0].Message);
    }
}
