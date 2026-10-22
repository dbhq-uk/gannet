using System.Globalization;
using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class BraveSearchTests
{
    // Hand-written from the documented response shape (web.results[].title, url, description, page_age); not a captured response.
    private const string Fixture = """
        { "type": "search", "web": { "type": "search", "results": [
            { "title": "Alpha", "url": "https://alpha.example/", "description": "An <strong>alpha</strong> result", "page_age": "2026-09-30T00:00:00" },
            { "title": "Bad", "url": "not a url", "description": "x" },
            { "title": "Beta", "url": "https://beta.example/", "description": "Beta" }
        ] } }
        """;

    private static BraveSearch Brave(StubHttpHandler handler, string? key = "BSA-test") =>
        new(handler.Client(), "brave", new ServiceOptions { ApiKey = key });

    [Fact]
    public async Task Sends_subscription_token_and_strips_tags_from_snippets()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));

        var hits = await Brave(handler).SearchAsync("a b", 2, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.search.brave.com/res/v1/web/search?q=a%20b&count=2", request.Uri.AbsoluteUri);
        Assert.Equal("BSA-test", request.Headers["X-Subscription-Token"]);
        Assert.Equal("An alpha result", hits[0].Snippet);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), hits[0].Published);
        Assert.Equal(2, hits.Count);
        Assert.Equal(["Alpha", "Beta"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task Titles_are_one_line_of_at_most_300_characters_and_snippets_at_most_2000()
    {
        var title = "First line\r\n\tsecond line\nthird " + new string('t', 500);
        var description = string.Join(' ', Enumerable.Repeat("gannet", 1000));
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(System.Text.Json.JsonSerializer.Serialize(new
        {
            web = new { results = new[] { new { title, url = "https://t.example/", description } } },
        })));

        var hit = Assert.Single(await Brave(handler).SearchAsync("q", 3, CancellationToken.None));

        Assert.StartsWith("First line second line third ", hit.Title, StringComparison.Ordinal);
        Assert.InRange(hit.Title.Length, 1, 300);
        Assert.DoesNotContain('\n', hit.Title);
        Assert.DoesNotContain('\r', hit.Title);
        Assert.DoesNotContain('\t', hit.Title);
        Assert.InRange(hit.Snippet.Length, 1, 2000);
        Assert.StartsWith("gannet gannet", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_key_is_an_auth_error_before_any_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Assert.ThrowsAsync<AuthRequiredException>(() => Brave(handler, key: null).SearchAsync("q", 2, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_key_is_never_in_the_url()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Brave(handler, "BSA-secret").SearchAsync("q", 3, CancellationToken.None);

        Assert.DoesNotContain("BSA-secret", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 20)]
    public async Task Max_results_stays_inside_what_brave_accepts(int asked, int sent)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Brave(handler).SearchAsync("q", asked, CancellationToken.None);

        Assert.EndsWith($"&count={sent.ToString(CultureInfo.InvariantCulture)}", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task No_results_wanted_sends_no_request(int asked)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await Brave(handler).SearchAsync("q", asked, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Bad_urls_are_skipped()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "web": { "results": [
                { "title": "Script", "url": "javascript:alert(1)", "description": "x" },
                { "title": "Userinfo", "url": "https://user:pw@host.example/", "description": "x" },
                { "title": "Missing", "description": "x" },
                { "title": "Good", "url": "https://good.example/", "description": "x" }
            ] } }
            """));
        var hits = await Brave(handler).SearchAsync("q", 5, CancellationToken.None);

        Assert.Equal("Good", Assert.Single(hits).Title);
    }

    [Fact]
    public async Task Duplicate_urls_keep_the_first_hit_before_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "web": { "results": [
                { "title": "First", "url": "https://dup.example/a", "description": "1" },
                { "title": "Again", "url": "https://dup.example/a", "description": "2" },
                { "title": "Other", "url": "https://other.example/", "description": "3" }
            ] } }
            """));
        var hits = await Brave(handler).SearchAsync("q", 2, CancellationToken.None);

        Assert.Equal(["First", "Other"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task A_response_with_no_web_section_is_no_hits()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "type": "search" }"""));

        Assert.Empty(await Brave(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "type": "ErrorResponse", "error": { "code": "SUBSCRIPTION_TOKEN_INVALID", "detail": "bad key BSA-secret" } }""", status));
        var error = await Assert.ThrowsAnyAsync<ServiceException>(() => Brave(handler, "BSA-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("BSA-secret", error.Message, StringComparison.Ordinal);
        if (status == HttpStatusCode.Unauthorized)
        {
            Assert.IsType<AuthRequiredException>(error);
        }
        else
        {
            Assert.IsType<ServiceFailedException>(error);
        }
    }

    // Brave documents these error codes (error.code) on a 429; QUOTA_LIMITED, USAGE_LIMIT_EXCEEDED and CREDIT_EXHAUSTED are plan limits, not a rate.
    [Theory]
    [InlineData("QUOTA_LIMITED")]
    [InlineData("USAGE_LIMIT_EXCEEDED")]
    [InlineData("CREDIT_EXHAUSTED")]
    public async Task Plan_and_credit_limits_are_quota_errors(string code)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($$"""{ "type": "ErrorResponse", "error": { "id": "x", "status": 429, "code": "{{code}}", "detail": "limit BSA-secret" } }""", HttpStatusCode.TooManyRequests));
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Brave(handler, "BSA-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.Contains("429", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("BSA-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(code, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rate_limit_is_typed_and_not_a_quota_error()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "type": "ErrorResponse", "error": { "code": "RATE_LIMITED" } }""", HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => Brave(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public async Task A_rate_limit_with_an_unreadable_body_is_still_a_rate_limit()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html>slow down</html>", status: HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => Brave(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_utf8_in_the_error_code_is_still_a_rate_limit()
    {
        byte[] body = [.. "{ \"error\": { \"code\": \"QUOTA_"u8, 0xFF, 0xFE, .. "\" } }"u8];
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new ByteArrayContent(body) });

        await Assert.ThrowsAsync<RateLimitedException>(() => Brave(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public async Task Many_unmatched_angle_brackets_in_a_description_are_handled_in_linear_time()
    {
        var description = new string('<', 1_000_000);
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($$"""{ "web": { "results": [ { "title": "T", "url": "https://t.example/", "description": "{{description}}" } ] } }"""));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var hits = await Brave(handler).SearchAsync("q", 3, CancellationToken.None);

        // Wide on purpose: the old quadratic pattern needs about 5x10^11 steps on this input, so a regression still fails by a long way.
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(60), $"took {started.Elapsed}");
        Assert.Equal(new string('<', 2000), Assert.Single(hits).Snippet);
    }
}
