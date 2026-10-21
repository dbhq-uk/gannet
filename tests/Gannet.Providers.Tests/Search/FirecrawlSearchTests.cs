using System.Globalization;
using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class FirecrawlSearchTests
{
    // Hand-written from the documented v2 response shape (data.web[].url, title, description, position); not a captured response.
    private const string Fixture = """
        { "success": true, "data": { "web": [
            { "url": "https://one.example/", "title": "One", "description": "one", "position": 1 },
            { "url": "https://two.example/", "title": "Two", "description": "two", "position": 2 }
        ] } }
        """;

    private static FirecrawlSearch Firecrawl(StubHttpHandler handler, ServiceOptions? options = null) =>
        new(handler.Client(), "firecrawl", options ?? new ServiceOptions { ApiKey = "fc-1" });

    [Fact]
    public async Task Keyed_search_sends_bearer_and_limit()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await Firecrawl(handler).SearchAsync("q", 2, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.firecrawl.dev/v2/search", request.Uri.ToString());
        Assert.Equal("Bearer fc-1", request.Headers["Authorization"]);
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(2, body.GetProperty("limit").GetInt32());
        Assert.Equal("q", body.GetProperty("query").GetString());
        Assert.Equal(["One", "Two"], hits.Select(h => h.Title));
        Assert.Equal("one", hits[0].Snippet);
    }

    [Fact]
    public async Task No_key_sends_no_authorization()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Firecrawl(handler, new ServiceOptions()).SearchAsync("q", 2, CancellationToken.None);

        Assert.False(Assert.Single(handler.Requests).Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Keyless_option_drops_the_key()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Firecrawl(handler, new ServiceOptions { ApiKey = "fc-secret", Keyless = true }).SearchAsync("q", 2, CancellationToken.None);

        Assert.False(Assert.Single(handler.Requests).Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task The_key_is_never_in_the_url()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Firecrawl(handler, new ServiceOptions { ApiKey = "fc-secret" }).SearchAsync("q", 2, CancellationToken.None);

        Assert.DoesNotContain("fc-secret", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(500, 100)]
    public async Task Limit_stays_inside_what_firecrawl_accepts(int asked, int sent)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Firecrawl(handler).SearchAsync("q", asked, CancellationToken.None);

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal(sent.ToString(CultureInfo.InvariantCulture), body.GetProperty("limit").GetInt32().ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_query_over_the_documented_500_characters_is_cut_to_500()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Firecrawl(handler).SearchAsync(new string('a', 900), 2, CancellationToken.None);

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal(500, body.GetProperty("query").GetString()!.Length);
    }

    [Fact]
    public async Task Cutting_a_long_query_never_splits_a_surrogate_pair()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var query = new string('a', 499) + "\U0001F600" + "tail";
        await Firecrawl(handler).SearchAsync(query, 2, CancellationToken.None);

        var sent = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement.GetProperty("query").GetString()!;
        Assert.Equal(new string('a', 499), sent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task No_results_wanted_sends_no_request(int asked)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await Firecrawl(handler).SearchAsync("q", asked, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Bad_urls_are_skipped()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "success": true, "data": { "web": [
                { "url": "javascript:alert(1)", "title": "Script", "description": "x" },
                { "url": "https://user:pw@host.example/", "title": "Userinfo", "description": "x" },
                { "title": "Missing", "description": "x" },
                { "url": "https://good.example/", "title": "Good", "description": "x" }
            ] } }
            """));
        var hits = await Firecrawl(handler).SearchAsync("q", 5, CancellationToken.None);

        Assert.Equal("Good", Assert.Single(hits).Title);
    }

    [Fact]
    public async Task Duplicate_urls_keep_the_first_hit_before_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "success": true, "data": { "web": [
                { "url": "https://dup.example/a", "title": "First", "description": "1" },
                { "url": "https://dup.example/a", "title": "Again", "description": "2" },
                { "url": "https://other.example/", "title": "Other", "description": "3" }
            ] } }
            """));
        var hits = await Firecrawl(handler).SearchAsync("q", 2, CancellationToken.None);

        Assert.Equal(["First", "Other"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task A_response_with_no_web_section_is_no_hits()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": true, "data": { "news": [] } }"""));

        Assert.Empty(await Firecrawl(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": false, "error": "Unauthorized: bad key fc-secret" }""", HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Firecrawl(handler, new ServiceOptions { ApiKey = "fc-secret" }).SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("fc-secret", error.Message, StringComparison.Ordinal);
    }

    // Documented on the Errors page: 402 "Payment Required: Insufficient credits" means plan credits are used up.
    [Fact]
    public async Task Insufficient_credits_is_a_quota_error_with_no_body_in_the_message()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": false, "error": "Payment Required: Insufficient credits fc-secret" }""", HttpStatusCode.PaymentRequired));
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Firecrawl(handler, new ServiceOptions { ApiKey = "fc-secret" }).SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("fc-secret", error.Message, StringComparison.Ordinal);
    }

    // Keyless use is capped per IP address per day and the docs say exceeding the cap returns a 429, so it is a rate limit.
    [Fact]
    public async Task The_keyless_daily_cap_is_a_rate_limit()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": false, "error": "Rate limit exceeded" }""", HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<RateLimitedException>(() => Firecrawl(handler, new ServiceOptions()).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public void Terms_say_results_may_not_be_stored_and_it_returns_no_page_content()
    {
        var firecrawl = Firecrawl(new StubHttpHandler(_ => StubHttpHandler.Json(Fixture)));

        Assert.False(firecrawl.Terms.MayPersistResults);
        Assert.False(firecrawl.ReturnsPageContent);
    }
}
