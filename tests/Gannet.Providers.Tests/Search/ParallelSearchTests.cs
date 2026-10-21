using System.Globalization;
using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class ParallelSearchTests
{
    // Hand-written from the documented v1 response shape (results[].url, title, publish_date, excerpts[]); not a captured response.
    private const string Fixture = """
        { "search_id": "s1", "session_id": "ss1", "results": [
            { "url": "https://one.example/", "title": "One", "publish_date": "2026-09-01", "excerpts": ["First excerpt.", "Second excerpt."] }
        ] }
        """;

    private static ParallelSearch Parallel(StubHttpHandler handler, string? key = "p-1") =>
        new(handler.Client(), "parallel", new ServiceOptions { ApiKey = key });

    [Fact]
    public async Task Sends_api_key_and_joins_excerpts_into_the_snippet()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var parallel = Parallel(handler);
        var hits = await parallel.SearchAsync("q", 5, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.parallel.ai/v1/search", request.Uri.ToString());
        Assert.Equal("p-1", request.Headers["x-api-key"]);
        Assert.Equal("First excerpt. Second excerpt.", Assert.Single(hits).Snippet);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), hits[0].Published);
        Assert.False(parallel.Terms.MayPersistResults);
        Assert.False(parallel.ReturnsPageContent);
    }

    // The documented request: search_queries (required), objective, and the result count inside advanced_settings.max_results.
    [Fact]
    public async Task Sends_the_query_as_objective_and_search_query_with_the_count_in_advanced_settings()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Parallel(handler).SearchAsync("a b", 5, CancellationToken.None);

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal("a b", body.GetProperty("objective").GetString());
        Assert.Equal(["a b"], body.GetProperty("search_queries").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(5, body.GetProperty("advanced_settings").GetProperty("max_results").GetInt32());
        Assert.False(body.TryGetProperty("max_results", out _));
    }

    [Fact]
    public async Task Missing_key_is_an_auth_error_before_any_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Assert.ThrowsAsync<AuthRequiredException>(() => Parallel(handler, key: null).SearchAsync("q", 2, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_key_is_never_in_the_url()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Parallel(handler, "p-secret").SearchAsync("q", 3, CancellationToken.None);

        Assert.DoesNotContain("p-secret", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 20)]
    public async Task Max_results_stays_inside_what_parallel_accepts(int asked, int sent)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Parallel(handler).SearchAsync("q", asked, CancellationToken.None);

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal(sent.ToString(CultureInfo.InvariantCulture), body.GetProperty("advanced_settings").GetProperty("max_results").GetInt32().ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task No_results_wanted_sends_no_request(int asked)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await Parallel(handler).SearchAsync("q", asked, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Bad_urls_are_skipped_and_non_string_excerpts_are_ignored()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "results": [
                { "url": "javascript:alert(1)", "title": "Script", "excerpts": ["x"] },
                { "url": "https://user:pw@host.example/", "title": "Userinfo", "excerpts": ["x"] },
                { "title": "Missing", "excerpts": ["x"] },
                { "url": "https://good.example/", "title": "Good", "excerpts": ["kept", 7, null, "also"] }
            ] }
            """));
        var hits = await Parallel(handler).SearchAsync("q", 5, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("Good", hit.Title);
        Assert.Equal("kept also", hit.Snippet);
    }

    [Fact]
    public async Task Duplicate_urls_keep_the_first_hit_before_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "results": [
                { "url": "https://dup.example/a", "title": "First", "excerpts": ["1"] },
                { "url": "https://dup.example/a", "title": "Again", "excerpts": ["2"] },
                { "url": "https://other.example/", "title": "Other", "excerpts": ["3"] }
            ] }
            """));
        var hits = await Parallel(handler).SearchAsync("q", 2, CancellationToken.None);

        Assert.Equal(["First", "Other"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task A_result_with_no_excerpts_has_an_empty_snippet()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [ { "url": "https://a.example/", "title": "A" } ] }"""));

        Assert.Equal(string.Empty, Assert.Single(await Parallel(handler).SearchAsync("q", 3, CancellationToken.None)).Snippet);
    }

    [Fact]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "type": "error", "error": { "ref_id": "r", "message": "bad key p-secret" } }""", HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Parallel(handler, "p-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("p-secret", error.Message, StringComparison.Ordinal);
    }

    // The docs name no quota code beyond the standard statuses, so 402 takes the standard mapping and 429 is a rate limit.
    [Fact]
    public async Task Payment_required_is_a_quota_error()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "type": "error", "error": { "message": "no credit p-secret" } }""", HttpStatusCode.PaymentRequired));
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Parallel(handler, "p-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("p-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_requests_is_a_rate_limit()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "type": "error" }""", HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<RateLimitedException>(() => Parallel(handler).SearchAsync("q", 3, CancellationToken.None));
    }
}
