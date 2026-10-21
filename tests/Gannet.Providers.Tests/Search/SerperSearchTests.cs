using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class SerperSearchTests
{
    // Hand-written from the documented response shape (organic[].title, link, snippet, date); not a captured response.
    private const string Fixture = """
        { "searchParameters": { "q": "q" }, "organic": [
            { "title": "One", "link": "https://one.example/", "snippet": "one", "position": 1 },
            { "title": "Bad", "link": "not a url", "snippet": "x", "position": 2 },
            { "title": "Two", "link": "https://two.example/", "snippet": "two", "date": "Oct 1, 2026", "position": 3 }
        ] }
        """;

    private static SerperSearch Serper(StubHttpHandler handler, string? key = "k") =>
        new(handler.Client(), "serper", new ServiceOptions { ApiKey = key });

    [Fact]
    public async Task Posts_query_with_api_key_header_and_maps_organic_results()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await Serper(handler).SearchAsync("q", 5, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://google.serper.dev/search", request.Uri.ToString());
        Assert.Equal("k", request.Headers["X-API-KEY"]);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("q", body.RootElement.GetProperty("q").GetString());
        Assert.Equal(5, body.RootElement.GetProperty("num").GetInt32());
        Assert.Equal(["One", "Two"], hits.Select(h => h.Title));
        Assert.Equal("one", hits[0].Snippet);
        Assert.Equal(new Uri("https://two.example/"), hits[1].Url);
        Assert.Null(hits[0].Published);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), hits[1].Published);
    }

    [Fact]
    public async Task A_relative_date_is_not_a_date()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "organic": [ { "title": "Recent", "link": "https://recent.example/", "snippet": "s", "date": "2 weeks ago" } ] }
            """));

        Assert.Null(Assert.Single(await Serper(handler).SearchAsync("q", 3, CancellationToken.None)).Published);
    }

    [Fact]
    public async Task Missing_key_is_an_auth_error_before_any_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Assert.ThrowsAsync<AuthRequiredException>(() => Serper(handler, key: null).SearchAsync("q", 2, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_key_is_never_in_the_url()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Serper(handler, "serper-secret").SearchAsync("q", 3, CancellationToken.None);

        Assert.DoesNotContain("serper-secret", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 10)]
    public async Task Max_results_stays_inside_what_serper_is_asked_for(int asked, int sent)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await Serper(handler).SearchAsync("q", asked, CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal(sent, body.RootElement.GetProperty("num").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task No_results_wanted_sends_no_request(int asked)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await Serper(handler).SearchAsync("q", asked, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Bad_urls_are_skipped()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "organic": [
                { "title": "Script", "link": "javascript:alert(1)", "snippet": "x" },
                { "title": "Userinfo", "link": "https://user:pw@host.example/", "snippet": "x" },
                { "title": "Missing", "snippet": "x" },
                { "title": "Good", "link": "https://good.example/", "snippet": "x" }
            ] }
            """));
        var hits = await Serper(handler).SearchAsync("q", 5, CancellationToken.None);

        Assert.Equal("Good", Assert.Single(hits).Title);
    }

    [Fact]
    public async Task Duplicate_urls_keep_the_first_hit_before_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "organic": [
                { "title": "First", "link": "https://dup.example/a", "snippet": "1" },
                { "title": "Again", "link": "https://dup.example/a", "snippet": "2" },
                { "title": "Other", "link": "https://other.example/", "snippet": "3" }
            ] }
            """));
        var hits = await Serper(handler).SearchAsync("q", 2, CancellationToken.None);

        Assert.Equal(["First", "Other"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task A_response_with_no_organic_section_is_no_hits()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "searchParameters": { "q": "q" } }"""));

        Assert.Empty(await Serper(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "message": "bad key serper-secret", "statusCode": 401 }""", status));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Serper(handler, "serper-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("serper-secret", error.Message, StringComparison.Ordinal);
    }

    // Not in Serper's public pages: out of credits is reported as a 400 with this message by people who have hit it (see the report).
    [Fact]
    public async Task Out_of_credits_is_a_quota_error()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "message": "Not enough credits", "statusCode": 400 }""", HttpStatusCode.BadRequest));
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Serper(handler, "serper-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.Contains("400", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("serper-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_bad_request_is_a_plain_failure_with_no_server_text()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "message": "Missing query parameter serper-secret", "statusCode": 400 }""", HttpStatusCode.BadRequest));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Serper(handler, "serper-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("serper-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing query", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_is_typed()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => Serper(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public void Type_and_name_are_set()
    {
        var serper = Serper(new StubHttpHandler(_ => StubHttpHandler.Json(Fixture)));

        Assert.Equal("serper", serper.Type);
        Assert.Equal("serper", serper.Name);
        Assert.False(serper.Terms.MayPersistResults);
    }

    [Fact]
    public async Task Invalid_utf8_in_the_error_message_is_a_plain_failure()
    {
        byte[] body = [.. "{ \"message\": \"Not enough "u8, 0xFF, 0xFE, .. "\" }"u8];
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new ByteArrayContent(body) });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Serper(handler).SearchAsync("q", 3, CancellationToken.None));
    }
}
