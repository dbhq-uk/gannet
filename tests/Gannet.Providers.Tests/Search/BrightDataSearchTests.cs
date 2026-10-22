using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class BrightDataSearchTests
{
    // Hand-written from the documented parsed shape (organic[].link, title, description, global_rank); not a captured response.
    private const string Parsed = """
        { "general": {}, "organic": [
            { "link": "https://one.example/", "title": "One", "description": "one", "global_rank": 1 },
            { "link": "https://two.example/", "title": "Two", "description": "two", "global_rank": 2 }
        ] }
        """;

    private static BrightDataSearch Create(StubHttpHandler handler, string? key = "bd", string? country = null) =>
        new(handler.Client(), "brightdata", new ServiceOptions { ApiKey = key, Zone = "serp_api1", Country = country });

    private static string GoogleUrl(StubHttpHandler handler)
    {
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        return body.RootElement.GetProperty("url").GetString()!;
    }

    [Fact]
    public async Task Posts_a_google_url_with_brd_json_to_the_zone()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Parsed));
        var hits = await new BrightDataSearch(handler.Client(), "brightdata", new ServiceOptions { ApiKey = "bd", Zone = "serp_api1", Country = "gb" })
            .SearchAsync("best pizza leeds", 5, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.brightdata.com/request", request.Uri.ToString());
        Assert.Equal("Bearer bd", request.Headers["Authorization"]);
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("serp_api1", body.GetProperty("zone").GetString());
        Assert.Equal("raw", body.GetProperty("format").GetString());
        Assert.Equal("https://www.google.com/search?q=best%20pizza%20leeds&brd_json=1&gl=gb&hl=en", body.GetProperty("url").GetString());
        Assert.Equal(["One", "Two"], hits.Select(h => h.Title));
        Assert.Equal("one", hits[0].Snippet);
        Assert.Equal(new Uri("https://two.example/"), hits[1].Url);
    }

    [Fact]
    public async Task Unwraps_a_status_and_body_envelope()
    {
        var envelope = JsonSerializer.Serialize(new { status_code = 200, headers = new { }, body = Parsed });
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(envelope));
        var hits = await new BrightDataSearch(handler.Client(), "brightdata", new ServiceOptions { ApiKey = "bd", Zone = "z" }).SearchAsync("q", 5, CancellationToken.None);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Zone_is_required()
    {
        Assert.Throws<ArgumentException>(() => new BrightDataSearch(new HttpClient(), "brightdata", new ServiceOptions { ApiKey = "bd" }));
        Assert.Throws<ArgumentException>(() => new BrightDataSearch(new HttpClient(), "brightdata", new ServiceOptions { ApiKey = "bd", Zone = "  " }));
    }

    [Theory]
    [InlineData(null, "gb")]
    [InlineData("", "gb")]
    [InlineData("us", "us")]
    [InlineData("DE", "de")]
    [InlineData("usa", "gb")]
    [InlineData("u", "gb")]
    [InlineData("g&", "gb")]
    [InlineData("gb&num=100", "gb")]
    [InlineData("g b", "gb")]
    [InlineData("%2f", "gb")]
    [InlineData("éé", "gb")]
    public async Task Country_is_two_letters_or_gb_and_never_alters_the_url(string? country, string sent)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Parsed));
        await Create(handler, country: country).SearchAsync("q", 5, CancellationToken.None);

        Assert.Equal($"https://www.google.com/search?q=q&brd_json=1&gl={sent}&hl=en", GoogleUrl(handler));
    }

    [Fact]
    public async Task The_query_is_escaped_so_it_cannot_add_parameters()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Parsed));
        await Create(handler).SearchAsync("a&gl=us#x", 5, CancellationToken.None);

        Assert.Equal("https://www.google.com/search?q=a%26gl%3Dus%23x&brd_json=1&gl=gb&hl=en", GoogleUrl(handler));
    }

    [Fact]
    public async Task Missing_key_is_an_auth_error_before_any_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Parsed));
        await Assert.ThrowsAsync<AuthRequiredException>(() => Create(handler, key: null).SearchAsync("q", 2, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_key_is_never_in_the_url()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Parsed));
        await Create(handler, "bd-secret").SearchAsync("q", 3, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("bd-secret", request.Uri.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("bd-secret", GoogleUrl(handler), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task No_results_wanted_sends_no_request(int asked)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Parsed));
        var hits = await Create(handler).SearchAsync("q", asked, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Bad_urls_are_skipped_and_duplicates_dropped_before_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "organic": [
                { "title": "Script", "link": "javascript:alert(1)", "description": "x" },
                { "title": "Userinfo", "link": "https://user:pw@host.example/", "description": "x" },
                { "title": "Missing", "description": "x" },
                { "title": "First", "link": "https://dup.example/a", "description": "1" },
                { "title": "Again", "link": "https://dup.example/a", "description": "2" },
                { "title": "Other", "link": "https://other.example/", "description": "3" }
            ] }
            """));
        var hits = await Create(handler).SearchAsync("q", 2, CancellationToken.None);

        Assert.Equal(["First", "Other"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task A_response_with_no_organic_section_is_no_hits()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "general": {} }"""));

        Assert.Empty(await Create(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "error": "bad token bd-secret" }""", status));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Create(handler, "bd-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("bd-secret", error.Message, StringComparison.Ordinal);
    }

    // Documented in the proxy error catalogue: client_10020 "Account is suspended" (automatic when the balance goes negative) and client_10100
    // "Zone has reached usage limit". On the Direct API the outer status can be 200, so the headers are the signal, not the status. The code is in
    // x-brd-err-code and, in the RFC 9209 Proxy-Status header, in details="<code>: ...".
    [Theory]
    [InlineData(HttpStatusCode.OK, "client_10020", false)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired, "client_10020", false)]
    [InlineData(HttpStatusCode.OK, "client_10100", false)]
    [InlineData(HttpStatusCode.BadGateway, "client_10100", false)]
    [InlineData(HttpStatusCode.OK, "client_10020", true)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired, "client_10020", true)]
    [InlineData(HttpStatusCode.BadGateway, "client_10100", true)]
    [InlineData(HttpStatusCode.OK, "client_10100", true)]
    public async Task A_suspended_account_or_a_zone_usage_limit_is_a_quota_error(HttpStatusCode status, string code, bool proxyStatus)
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text("Zone has reached usage limit bd-secret", "text/plain", status);
            if (proxyStatus)
            {
                response.Headers.TryAddWithoutValidation("Proxy-Status", $"brd.superproxy.io; received-status=502; error=\"http_request_denied\"; details=\"{code}: text bd-secret\"");
            }
            else
            {
                response.Headers.Add("x-brd-err-code", code);
            }

            return response;
        });
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Create(handler, "bd-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("bd-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("usage limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("suspended", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_proxy_error_code_is_not_a_quota_error()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text("denied", "text/plain", HttpStatusCode.ProxyAuthenticationRequired);
            response.Headers.Add("x-brd-err-code", "client_10000");
            return response;
        });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Create(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public async Task Another_proxy_status_code_is_not_a_quota_error()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text("denied", "text/plain", HttpStatusCode.ProxyAuthenticationRequired);
            response.Headers.TryAddWithoutValidation("Proxy-Status", "brd.superproxy.io; received-status=407; error=\"http_request_denied\"; details=\"client_10000: Invalid authentication, and a mention of client_10100\"");
            return response;
        });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Create(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    // On the Direct API an unlock that failed still answers 200; the failure is in x-brd-error, x-brd-error-code or x-brd-err-code (the reader checks the same three).
    [Theory]
    [InlineData("x-brd-error")]
    [InlineData("x-brd-error-code")]
    [InlineData("x-brd-err-code")]
    public async Task A_200_that_carries_a_bright_data_error_header_is_a_failure_and_not_results(string header)
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Json(Parsed);
            response.Headers.Add(header, "brd-secret-detail");
            return response;
        });

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Create(handler).SearchAsync("q", 3, CancellationToken.None));

        Assert.Equal("Bright Data SERP could not return results.", error.Message);
    }

    [Fact]
    public async Task Rate_limit_is_typed()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => Create(handler).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public async Task A_body_that_is_not_parsed_results_fails_without_quoting_any_of_it()
    {
        var envelope = JsonSerializer.Serialize(new { status_code = 200, body = "<html>page text secret-page-text bd-secret</html>" });
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(envelope));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Create(handler, "bd-secret").SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("secret-page-text", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bd-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("<html>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_outer_body_that_is_not_json_fails_without_quoting_any_of_it()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html>secret-page-text</html>"));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Create(handler).SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("secret-page-text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Type_and_terms_are_set()
    {
        var search = Create(new StubHttpHandler(_ => StubHttpHandler.Json(Parsed)));

        Assert.Equal("brightdata", search.Type);
        Assert.Equal("brightdata", search.Name);
        Assert.False(search.Terms.MayPersistResults);
    }
}
