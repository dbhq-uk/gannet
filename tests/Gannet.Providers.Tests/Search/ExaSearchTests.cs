using System.Globalization;
using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class ExaSearchTests
{
    // Hand-written from the documented response shape (results[].title, url, text, publishedDate); not a captured response.
    private const string Fixture = """
        { "requestId": "r1", "results": [
            { "title": "One", "url": "https://one.example/", "text": "Full text of one.", "publishedDate": "2026-09-01T00:00:00.000Z" }
        ] }
        """;

    private static ExaSearch Exa(StubHttpHandler handler, string? key = "e-1") =>
        new(handler.Client(), "exa", new ServiceOptions { ApiKey = key });

    [Fact]
    public async Task Requests_text_and_returns_it_as_page_content()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var exa = Exa(handler);
        var hits = await exa.SearchAsync("q", 4, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.exa.ai/search", request.Uri.ToString());
        Assert.Equal("e-1", request.Headers["x-api-key"]);
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("q", body.GetProperty("query").GetString());
        Assert.Equal(4, body.GetProperty("numResults").GetInt32());
        Assert.Equal(8000, body.GetProperty("contents").GetProperty("text").GetProperty("maxCharacters").GetInt32());
        Assert.Equal("Full text of one.", Assert.Single(hits).Content);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), hits[0].Published);
        Assert.True(exa.ReturnsPageContent);
        Assert.False(exa.Terms.MayPersistResults);
    }

    [Fact]
    public async Task The_snippet_is_the_first_300_characters_of_the_text()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($$"""{ "results": [ { "title": "T", "url": "https://t.example/", "text": "{{new string('x', 900)}}" } ] }"""));
        var hit = Assert.Single(await Exa(handler).SearchAsync("q", 3, CancellationToken.None));

        Assert.Equal(300, hit.Snippet.Length);
        Assert.Equal(900, hit.Content!.Length);
    }

    [Fact]
    public async Task A_result_with_no_text_has_no_page_content()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [ { "title": "T", "url": "https://t.example/" } ] }"""));
        var hit = Assert.Single(await Exa(handler).SearchAsync("q", 3, CancellationToken.None));

        Assert.Null(hit.Content);
        Assert.Equal(string.Empty, hit.Snippet);
    }

    [Fact]
    public async Task A_plain_date_is_read_as_the_published_date()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [ { "title": "T", "url": "https://t.example/", "text": "x", "publishedDate": "2026-09-01" } ] }"""));
        var hit = Assert.Single(await Exa(handler).SearchAsync("q", 3, CancellationToken.None));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), hit.Published);
    }

    [Fact]
    public async Task Missing_key_is_an_auth_error_before_any_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Assert.ThrowsAsync<AuthRequiredException>(() => Exa(handler, key: null).SearchAsync("q", 2, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_key_is_never_in_the_url()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Exa(handler, "e-secret").SearchAsync("q", 3, CancellationToken.None);

        Assert.DoesNotContain("e-secret", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(500, 100)]
    public async Task Num_results_stays_inside_what_exa_accepts(int asked, int sent)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Exa(handler).SearchAsync("q", asked, CancellationToken.None);

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal(sent.ToString(CultureInfo.InvariantCulture), body.GetProperty("numResults").GetInt32().ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task No_results_wanted_sends_no_request(int asked)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await Exa(handler).SearchAsync("q", asked, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Bad_urls_are_skipped()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "results": [
                { "title": "Script", "url": "javascript:alert(1)", "text": "x" },
                { "title": "Userinfo", "url": "https://user:pw@host.example/", "text": "x" },
                { "title": "Missing", "text": "x" },
                { "title": "Good", "url": "https://good.example/", "text": "x" }
            ] }
            """));
        var hits = await Exa(handler).SearchAsync("q", 5, CancellationToken.None);

        Assert.Equal("Good", Assert.Single(hits).Title);
    }

    [Fact]
    public async Task Duplicate_urls_keep_the_first_hit_before_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "results": [
                { "title": "First", "url": "https://dup.example/a", "text": "1" },
                { "title": "Again", "url": "https://dup.example/a", "text": "2" },
                { "title": "Other", "url": "https://other.example/", "text": "3" }
            ] }
            """));
        var hits = await Exa(handler).SearchAsync("q", 2, CancellationToken.None);

        Assert.Equal(["First", "Other"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "requestId": "r", "error": "bad key e-secret", "tag": "INVALID_API_KEY" }""", HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Exa(handler, "e-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("e-secret", error.Message, StringComparison.Ordinal);
    }

    // The spec documents 402 for a team out of credits or over a spending budget.
    [Fact]
    public async Task Out_of_credits_is_a_quota_error()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "requestId": "r", "error": "no credits e-secret", "tag": "NO_MORE_CREDITS" }""", HttpStatusCode.PaymentRequired));
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Exa(handler, "e-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("e-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_requests_is_a_rate_limit()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "requestId": "r", "error": "slow", "tag": "RATE_LIMIT" }""", HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<RateLimitedException>(() => Exa(handler).SearchAsync("q", 3, CancellationToken.None));
    }
}
