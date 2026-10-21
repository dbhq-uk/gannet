using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Gannet.Providers.Tests.Search;

public class DuckDuckGoSearchTests
{
    private const int Sixteen = 16 * 1024 * 1024;

    // Hand-written page in the shape of DuckDuckGo's HTML endpoint (a.result__a and a.result__snippet inside div.result; ads add result--ad).
    private const string Page = """
        <html><body>
          <div class="result result--ad"><a class="result__a" href="https://ads.example/">Ad</a><a class="result__snippet">ad</a></div>
          <div class="result"><a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fone.example%2Fpage&amp;rut=x">One</a><a class="result__snippet">One snippet</a></div>
          <div class="result"><a class="result__a" href="https://two.example/">Two</a><a class="result__snippet">Two snippet</a></div>
        </body></html>
        """;

    private static DuckDuckGoSearch Create(StubHttpHandler handler) =>
        new(handler.Client(), "duckduckgo", new ServiceOptions(), TimeProvider.System, TimeSpan.Zero);

    private static string Result(string href) =>
        $"""<html><body><div class="result"><a class="result__a" href="{href}">T</a><a class="result__snippet">s</a></div></body></html>""";

    [Fact]
    public async Task Parses_results_unwraps_redirects_and_skips_ads()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Page));
        var hits = await Create(handler).SearchAsync("q", 5, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
        Assert.Equal([new Uri("https://one.example/page"), new Uri("https://two.example/")], hits.Select(h => h.Url));
        Assert.Equal("One snippet", hits[0].Snippet);
        Assert.Equal("Two", hits[1].Title);
    }

    [Fact]
    public async Task Posts_the_query_as_a_form_to_the_html_endpoint()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Page));
        await Create(handler).SearchAsync("cats & dogs", 5, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://html.duckduckgo.com/html/", request.Uri.ToString());
        Assert.Equal("q=cats+%26+dogs", request.Body);
    }

    [Fact]
    public async Task Stops_at_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Page));

        Assert.Equal(new Uri("https://one.example/page"), Assert.Single(await Create(handler).SearchAsync("q", 1, CancellationToken.None)).Url);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task No_results_wanted_sends_no_request(int asked)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Page));

        Assert.Empty(await Create(handler).SearchAsync("q", asked, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://host.example/file")]
    [InlineData("https://user:pw@host.example/")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task A_redirect_target_that_is_not_a_plain_web_url_is_skipped(string target)
    {
        var href = "//duckduckgo.com/l/?uddg=" + Uri.EscapeDataString(target);
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Result(href)));

        Assert.Empty(await Create(handler).SearchAsync("q", 5, CancellationToken.None));
    }

    [Theory]
    [InlineData("//duckduckgo.com/l/?rut=x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pw@host.example/")]
    [InlineData("/relative")]
    [InlineData("")]
    public async Task A_link_that_is_not_a_plain_web_url_is_skipped(string href)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Result(href)));

        Assert.Empty(await Create(handler).SearchAsync("q", 5, CancellationToken.None));
    }

    [Fact]
    public async Task Only_duckduckgo_own_redirect_is_unwrapped()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Result("https://notduckduckgo.com/l/?uddg=https%3A%2F%2Fother.example%2F")));
        var hit = Assert.Single(await Create(handler).SearchAsync("q", 5, CancellationToken.None));

        Assert.Equal("notduckduckgo.com", hit.Url.Host);
    }

    [Fact]
    public async Task The_same_page_twice_is_one_hit()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("""
            <div class="result"><a class="result__a" href="https://dup.example/a">First</a></div>
            <div class="result"><a class="result__a" href="https://dup.example/a">Again</a></div>
            """));

        Assert.Equal("First", Assert.Single(await Create(handler).SearchAsync("q", 5, CancellationToken.None)).Title);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, "<html>anything</html>")]
    [InlineData(HttpStatusCode.OK, "<html><body>Unfortunately, bots use DuckDuckGo too.</body></html>")]
    public async Task Bot_challenge_is_typed(HttpStatusCode status, string body)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(body, status: status));
        await Assert.ThrowsAsync<ChallengeDetectedException>(() => Create(handler).SearchAsync("q", 5, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refusal_is_a_challenge_with_no_server_text(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html>secret-page-text</html>", status: status));
        var error = await Assert.ThrowsAsync<ChallengeDetectedException>(() => Create(handler).SearchAsync("q", 5, CancellationToken.None));

        Assert.DoesNotContain("secret-page-text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_and_server_errors_are_typed()
    {
        var limited = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => Create(limited).SearchAsync("q", 5, CancellationToken.None));

        var broken = new StubHttpHandler(_ => StubHttpHandler.Text("<html>secret-page-text</html>", status: HttpStatusCode.InternalServerError));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Create(broken).SearchAsync("q", 5, CancellationToken.None));
        Assert.DoesNotContain("secret-page-text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_over_the_cap_is_refused_on_its_declared_length()
    {
        var content = new ByteArrayContent(new byte[Sixteen + 1]);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Create(handler).SearchAsync("q", 5, CancellationToken.None));
    }

    [Fact]
    public async Task A_page_over_the_cap_is_refused_when_no_length_is_declared()
    {
        var content = new UnknownLengthContent(Sixteen + 1);
        Assert.Null(content.Headers.ContentLength);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Create(handler).SearchAsync("q", 5, CancellationToken.None));
    }

    [Fact]
    public async Task A_page_at_the_cap_is_read()
    {
        var padding = new string(' ', Sixteen - Page.Length);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(System.Text.Encoding.UTF8.GetBytes(Page + padding)) });

        Assert.Equal(2, (await Create(handler).SearchAsync("q", 5, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Requests_are_at_least_the_minimum_gap_apart()
    {
        // Start an hour ahead so an earlier request in this process cannot hold the first one back.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Page));
        var search = new DuckDuckGoSearch(handler.Client(), "duckduckgo", new ServiceOptions(), clock, TimeSpan.FromSeconds(1));

        await search.SearchAsync("one", 5, CancellationToken.None);
        var second = search.SearchAsync("two", 5, CancellationToken.None);

        Assert.False(second.IsCompleted);
        Assert.Single(handler.Requests);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(second.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await second.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_clock_that_is_behind_the_last_request_never_waits_longer_than_the_gap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(Page));
        var ahead = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(2));
        await new DuckDuckGoSearch(handler.Client(), "duckduckgo", new ServiceOptions(), ahead, TimeSpan.FromSeconds(1)).SearchAsync("one", 5, CancellationToken.None);

        var behind = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-2));
        var second = new DuckDuckGoSearch(handler.Client(), "duckduckgo", new ServiceOptions(), behind, TimeSpan.FromSeconds(1)).SearchAsync("two", 5, CancellationToken.None);
        Assert.False(second.IsCompleted);
        behind.Advance(TimeSpan.FromSeconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Type_and_terms_are_set()
    {
        var search = Create(new StubHttpHandler(_ => StubHttpHandler.Text(Page)));

        Assert.Equal("duckduckgo", search.Type);
        Assert.Equal("duckduckgo", search.Name);
        Assert.False(search.Terms.MayPersistResults);
    }

    /// <summary>A body with no Content-Length, so only the counted read can stop it.</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        public UnknownLengthContent(int size)
            : this(new byte[size])
        {
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
