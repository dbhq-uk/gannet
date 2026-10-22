using System.Globalization;
using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Readers;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Readers;

public class RemoteReadersTests
{
    private const string Secret = "sk-remote-secret";

    private static readonly Uri Page = new("https://site.example/article?id=1");

    // Every response body here is written by hand from the documented response shape; none is a captured response.
    public static TheoryData<string> Kinds => ["jina", "firecrawl", "tavily", "brightdata", "parallel", "exa"];

    public static TheoryData<string> KeyedKinds => ["brightdata", "parallel", "exa"];

    // The readers whose response has a title field. Tavily and Bright Data return none, so their title is the host.
    public static TheoryData<string> TitledKinds => ["jina", "firecrawl", "parallel", "exa"];

    private static Task<IPAddress[]> PublicDns(string host, CancellationToken ct) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

    private static IPageReader Create(string kind, StubHttpHandler handler, string? key = Secret, bool keyless = false, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        var options = new ServiceOptions { ApiKey = key, Zone = "unlocker1", Keyless = keyless };
        var http = handler.Client();
        resolve ??= PublicDns;
        return kind switch
        {
            "jina" => new JinaReader(http, "jina", options, resolve),
            "firecrawl" => new FirecrawlScrapeReader(http, "firecrawl", options, resolve),
            "tavily" => new TavilyExtractReader(http, "tavily", options, resolve),
            "brightdata" => new BrightDataUnlockerReader(http, "brightdata", options, resolve),
            "parallel" => new ParallelExtractReader(http, "parallel", options, resolve),
            "exa" => new ExaContentsReader(http, "exa", options, resolve),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string Quote(string text) => JsonSerializer.Serialize(text);

    private static HttpResponseMessage Ok(string kind, string text, string title = "T")
    {
        var t = Quote(title);
        var c = Quote(text);
        return kind switch
        {
            "jina" => StubHttpHandler.Json($$"""{ "code": 200, "status": 20000, "data": { "title": {{t}}, "url": "https://site.example/article?id=1", "content": {{c}} } }"""),
            "firecrawl" => StubHttpHandler.Json($$"""{ "success": true, "data": { "markdown": {{c}}, "metadata": { "title": {{t}}, "statusCode": 200 } } }"""),
            "tavily" => StubHttpHandler.Json($$"""{ "results": [ { "url": "https://site.example/article?id=1", "raw_content": {{c}} } ], "failed_results": [] }"""),
            "brightdata" => StubHttpHandler.Text(text, "text/plain"),
            "parallel" => StubHttpHandler.Json($$"""{ "extract_id": "e1", "results": [ { "url": "https://site.example/article?id=1", "title": {{t}}, "full_content": {{c}} } ], "errors": [] }"""),
            "exa" => StubHttpHandler.Json($$"""{ "requestId": "r1", "results": [ { "url": "https://site.example/article?id=1", "title": {{t}}, "text": {{c}} } ] }"""),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static Task<PageContent> Read(IPageReader reader, int max = 8000) => reader.ReadAsync(Page, max, CancellationToken.None);

    private static JsonElement Body(StubHttpHandler handler) => JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;

    [Fact]
    public async Task Jina_reads_keyless_as_json_markdown()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "code": 200, "status": 20000, "data": { "title": "T", "url": "https://site.example/article?id=1", "content": "# T\n\nbody" } }"""));
        var page = await new JinaReader(handler.Client(), "jina", new ServiceOptions(), PublicDns).ReadAsync(Page, 8000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://r.jina.ai/https://site.example/article?id=1", request.Uri.ToString());
        Assert.Equal("markdown", request.Headers["X-Respond-With"]);
        Assert.Equal("application/json", request.Headers["Accept"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.Equal(("T", "# T\n\nbody", "jina"), (page.Title, page.Markdown, page.Reader));
    }

    [Fact]
    public async Task Jina_drops_the_fragment_and_keeps_the_query()
    {
        var handler = new StubHttpHandler(_ => Ok("jina", "body"));
        await new JinaReader(handler.Client(), "jina", new ServiceOptions(), PublicDns)
            .ReadAsync(new Uri("https://site.example/a/b?x=1&y=two#section-3"), 8000, CancellationToken.None);

        Assert.Equal("https://r.jina.ai/https://site.example/a/b?x=1&y=two", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Jina_sends_a_unicode_host_as_punycode()
    {
        var handler = new StubHttpHandler(_ => Ok("jina", "body"));
        await new JinaReader(handler.Client(), "jina", new ServiceOptions(), PublicDns)
            .ReadAsync(new Uri("https://b\u00FCcher.example/x?y=1"), 8000, CancellationToken.None);

        Assert.Equal("https://r.jina.ai/https://xn--bcher-kva.example/x?y=1", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Theory]
    [InlineData("https://site.example:8443/a?b=c#frag", "https://r.jina.ai/https://site.example:8443/a?b=c")]
    [InlineData("http://site.example/", "https://r.jina.ai/http://site.example/")]
    [InlineData("https://site.example", "https://r.jina.ai/https://site.example/")]
    [InlineData("http://site.example:80/p", "https://r.jina.ai/http://site.example/p")]
    public async Task Jina_keeps_a_non_default_port_and_drops_a_default_one(string target, string expected)
    {
        var handler = new StubHttpHandler(_ => Ok("jina", "body"));
        await new JinaReader(handler.Client(), "jina", new ServiceOptions(), PublicDns).ReadAsync(new Uri(target), 8000, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Jina_sends_a_configured_key_as_a_bearer_token_only()
    {
        var handler = new StubHttpHandler(_ => Ok("jina", "body"));
        await Read(Create("jina", handler));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"Bearer {Secret}", request.Headers["Authorization"]);
        Assert.DoesNotContain(Secret, request.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Jina_keyless_option_drops_a_configured_key()
    {
        var handler = new StubHttpHandler(_ => Ok("jina", "body"));
        await Read(Create("jina", handler, keyless: true));

        Assert.False(Assert.Single(handler.Requests).Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Firecrawl_scrape_asks_for_markdown()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": true, "data": { "markdown": "body", "metadata": { "title": "T" } } }"""));
        var page = await new FirecrawlScrapeReader(handler.Client(), "firecrawl", new ServiceOptions { ApiKey = "fc" }, PublicDns).ReadAsync(Page, 8000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.firecrawl.dev/v2/scrape", request.Uri.ToString());
        Assert.Equal("Bearer fc", request.Headers["Authorization"]);
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("markdown", body.GetProperty("formats")[0].GetString());
        Assert.Equal(Page.ToString(), body.GetProperty("url").GetString());
        Assert.Equal(("T", "body"), (page.Title, page.Markdown));
    }

    [Fact]
    public async Task Firecrawl_keyless_sends_no_authorization_header()
    {
        var handler = new StubHttpHandler(_ => Ok("firecrawl", "body"));
        await Read(Create("firecrawl", handler, key: null));
        Assert.False(Assert.Single(handler.Requests).Headers.ContainsKey("Authorization"));

        var withKey = new StubHttpHandler(_ => Ok("firecrawl", "body"));
        await Read(Create("firecrawl", withKey, keyless: true));
        Assert.False(Assert.Single(withKey.Requests).Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Firecrawl_title_can_be_a_list_of_strings()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": true, "data": { "markdown": "body", "metadata": { "title": [ "First", "Second" ] } } }"""));
        var page = await Read(Create("firecrawl", handler));

        Assert.Equal("First", page.Title);
    }

    [Fact]
    public async Task Firecrawl_page_status_of_an_error_is_a_failure()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": true, "data": { "markdown": "Not found page text", "metadata": { "title": "404", "statusCode": 404 } } }"""));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create("firecrawl", handler)));

        Assert.Contains("404", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Not found page text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Firecrawl_success_false_is_a_failure()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "success": false, "error": "page text secret" }"""));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create("firecrawl", handler)));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Firecrawl_keyless_daily_cap_is_a_quota_error_but_a_keyed_429_is_a_rate_limit()
    {
        var keyless = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<QuotaExhaustedException>(() => Read(Create("firecrawl", keyless, key: null)));

        var keyed = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => Read(Create("firecrawl", keyed)));
    }

    [Fact]
    public async Task Tavily_extract_keyless_reads_raw_content()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [ { "url": "https://site.example/article?id=1", "raw_content": "body" } ], "failed_results": [] }"""));
        var page = await new TavilyExtractReader(handler.Client(), "tavily", new ServiceOptions(), PublicDns).ReadAsync(Page, 8000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.tavily.com/extract", request.Uri.ToString());
        Assert.Equal("keyless", request.Headers["X-Tavily-Access-Mode"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(Page.ToString(), body.GetProperty("urls")[0].GetString());
        Assert.Equal("markdown", body.GetProperty("format").GetString());
        Assert.Equal("body", page.Markdown);
        Assert.Equal("site.example", page.Title);
    }

    [Fact]
    public async Task Tavily_extract_with_a_key_sends_a_bearer_token_and_no_keyless_header()
    {
        var handler = new StubHttpHandler(_ => Ok("tavily", "body"));
        await Read(Create("tavily", handler));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"Bearer {Secret}", request.Headers["Authorization"]);
        Assert.False(request.Headers.ContainsKey("X-Tavily-Access-Mode"));
    }

    [Fact]
    public async Task Tavily_extract_keyless_option_drops_a_configured_key()
    {
        var handler = new StubHttpHandler(_ => Ok("tavily", "body"));
        await Read(Create("tavily", handler, keyless: true));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("keyless", request.Headers["X-Tavily-Access-Mode"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Tavily_extract_failure_is_typed()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [], "failed_results": [ { "url": "https://site.example/article?id=1", "error": "x tvly-secret" } ] }"""));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => new TavilyExtractReader(handler.Client(), "tavily", new ServiceOptions(), PublicDns).ReadAsync(Page, 8000, CancellationToken.None));

        Assert.DoesNotContain("tvly-secret", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(432)]
    [InlineData(433)]
    public async Task Tavily_plan_and_credit_limits_are_quota_errors(int status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($$"""{ "detail": "limit {{Secret}}" }""", (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Read(Create("tavily", handler)));

        Assert.Contains(status.ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bright_data_unlocker_returns_the_markdown_body()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("# T\n\nbody", "text/plain"));
        var page = await new BrightDataUnlockerReader(handler.Client(), "brightdata", new ServiceOptions { ApiKey = "bd", Zone = "unlocker1" }, PublicDns).ReadAsync(Page, 8000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.brightdata.com/request", request.Uri.ToString());
        Assert.Equal("Bearer bd", request.Headers["Authorization"]);
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("unlocker1", body.GetProperty("zone").GetString());
        Assert.Equal("raw", body.GetProperty("format").GetString());
        Assert.Equal("markdown", body.GetProperty("data_format").GetString());
        Assert.Equal(Page.ToString(), body.GetProperty("url").GetString());
        Assert.Equal("# T\n\nbody", page.Markdown);
        Assert.Equal("site.example", page.Title);
    }

    [Fact]
    public void Bright_data_unlocker_needs_a_zone()
    {
        var error = Assert.Throws<ArgumentException>(() => new BrightDataUnlockerReader(new StubHttpHandler(_ => Ok("brightdata", "x")).Client(), "bd", new ServiceOptions { ApiKey = "bd" }));

        Assert.Contains("Gannet:Readers:Services:bd:Zone", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x-brd-error-code", "reject_block")]
    [InlineData("x-brd-error", "captcha or protection page found")]
    [InlineData("x-brd-err-code", "policy_20020")]
    public async Task Bright_data_error_headers_on_a_200_are_a_failure_with_fixed_text(string header, string value)
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text("page text secret", "text/plain");
            response.Headers.Add(header, value);
            return response;
        });
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create("brightdata", handler)));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bright_data_target_error_status_is_a_failure()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text("# Not found\n\npage text secret", "text/plain");
            response.Headers.Add("x-brd-status-code", "404");
            return response;
        });
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create("brightdata", handler)));

        Assert.Contains("404", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bright_data_target_success_status_is_read()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text("body", "text/plain");
            response.Headers.Add("x-brd-status-code", "200");
            return response;
        });

        Assert.Equal("body", (await Read(Create("brightdata", handler))).Markdown);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "client_10020", false)]
    [InlineData(HttpStatusCode.OK, "client_10100", false)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired, "client_10020", false)]
    [InlineData(HttpStatusCode.OK, "client_10100", true)]
    [InlineData(HttpStatusCode.BadGateway, "client_10020", true)]
    public async Task Bright_data_account_and_zone_limits_are_quota_errors(HttpStatusCode status, string code, bool proxyStatus)
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text($"Zone has reached usage limit {Secret}", "text/plain", status);
            if (proxyStatus)
            {
                response.Headers.TryAddWithoutValidation("Proxy-Status", $"brd.superproxy.io; received-status=502; error=\"http_request_denied\"; details=\"{code}: text {Secret}\"");
            }
            else
            {
                response.Headers.Add("x-brd-err-code", code);
            }

            return response;
        });
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Read(Create("brightdata", handler)));

        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("usage limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bright_data_other_proxy_code_is_not_a_quota_error()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Text("denied", "text/plain", HttpStatusCode.ProxyAuthenticationRequired);
            response.Headers.TryAddWithoutValidation("Proxy-Status", "brd.superproxy.io; received-status=407; details=\"client_10000: Invalid auth, and a mention of client_10100\"");
            return response;
        });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create("brightdata", handler)));
    }

    [Fact]
    public async Task Bright_data_body_over_five_megabytes_is_refused()
    {
        var body = new CountingStream(64 * 1024 * 1024);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create("brightdata", handler)));

        Assert.True(body.BytesRead <= (5 * 1024 * 1024) + (256 * 1024), $"read {body.BytesRead} bytes");
        Assert.True(body.Disposed);
    }

    [Fact]
    public async Task Parallel_extract_reads_full_content()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [ { "url": "https://site.example/article?id=1", "title": "T", "full_content": "body" } ] }"""));
        var page = await new ParallelExtractReader(handler.Client(), "parallel", new ServiceOptions { ApiKey = "p" }, PublicDns).ReadAsync(Page, 8000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("p", request.Headers["x-api-key"]);
        Assert.Equal("https://api.parallel.ai/v1/extract", request.Uri.ToString());
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(Page.ToString(), body.GetProperty("urls")[0].GetString());
        Assert.True(body.GetProperty("advanced_settings").GetProperty("full_content").GetBoolean());

        // On /v1/extract a top-level full_content is a 422.
        Assert.False(body.TryGetProperty("full_content", out _));
        Assert.Equal(("T", "body"), (page.Title, page.Markdown));
    }

    [Fact]
    public async Task Parallel_extract_url_that_failed_is_a_failure()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [], "errors": [ { "url": "https://site.example/article?id=1", "error_type": "fetch_error", "http_status_code": 500, "content": "page text secret" } ] }"""));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create("parallel", handler)));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parallel_extract_endpoint_option_is_a_base_address()
    {
        var handler = new StubHttpHandler(_ => Ok("parallel", "body"));
        var options = new ServiceOptions { ApiKey = "p", Endpoint = "https://eu.parallel.ai/" };
        await new ParallelExtractReader(handler.Client(), "parallel", options, PublicDns).ReadAsync(Page, 8000, CancellationToken.None);

        Assert.Equal("https://eu.parallel.ai/v1/extract", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Exa_contents_reads_text()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [ { "url": "https://site.example/article?id=1", "title": "T", "text": "body" } ] }"""));
        var page = await new ExaContentsReader(handler.Client(), "exa", new ServiceOptions { ApiKey = "e" }, PublicDns).ReadAsync(Page, 8000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.exa.ai/contents", request.Uri.ToString());
        Assert.Equal("e", request.Headers["x-api-key"]);
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(Page.ToString(), body.GetProperty("urls")[0].GetString());
        Assert.True(body.GetProperty("text").GetBoolean());
        Assert.Equal(("T", "body"), (page.Title, page.Markdown));
    }

    [Fact]
    public async Task Remote_readers_cap_text()
    {
        var text = new string('x', 500) + " " + new string('y', 500);
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(text, "text/plain"));
        var page = await new BrightDataUnlockerReader(handler.Client(), "brightdata", new ServiceOptions { ApiKey = "bd", Zone = "z" }, PublicDns).ReadAsync(Page, 600, CancellationToken.None);
        Assert.True(page.Markdown.Length <= 600);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Every_reader_cuts_text_to_the_cap_without_splitting_a_surrogate_pair(string kind)
    {
        var text = new string('a', 599) + "\U0001F600" + new string('b', 400);
        var handler = new StubHttpHandler(_ => Ok(kind, text));
        var page = await Read(Create(kind, handler), 600);

        Assert.True(page.Markdown.Length <= 600);
        Assert.False(char.IsHighSurrogate(page.Markdown[^1]));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Every_reader_names_itself_in_the_page(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "  body\n\n"));
        var page = await Read(Create(kind, handler));

        Assert.Equal(kind, page.Reader);
        Assert.Equal("body", page.Markdown);
        Assert.Equal(Page, page.Url);
    }

    [Theory]
    [MemberData(nameof(TitledKinds))]
    public async Task A_long_title_is_cut_and_kept_on_one_line(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "body", "line one\nline two " + new string('t', 5000)));
        var page = await Read(Create(kind, handler));

        Assert.True(page.Title.Length <= 300);
        Assert.DoesNotContain('\n', page.Title);
        Assert.StartsWith("line one line two", page.Title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tavily")]
    [InlineData("brightdata")]
    public async Task A_reader_whose_service_sends_no_title_uses_the_host(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "body"));
        var page = await Read(Create(kind, handler));

        Assert.Equal("site.example", page.Title);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Empty_content_is_a_typed_failure_so_the_chain_moves_on(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "  \n "));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create(kind, handler)));

        Assert.EndsWith("returned no text for the page.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body(string kind)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($$"""{ "detail": "bad key {{Secret}} body text" }""", HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Read(Create(kind, handler)));

        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("body text", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_key_is_never_in_the_url(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "body"));
        await Read(Create(kind, handler));

        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain(Secret, request.Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(KeyedKinds))]
    public async Task A_reader_that_needs_a_key_says_so_before_any_request(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "body"));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Read(Create(kind, handler, key: null)));

        Assert.Contains($"Gannet:Readers:Services:{kind}:ApiKey", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Rate_limit_is_typed(string kind)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => Read(Create(kind, handler)));
    }

    [Theory]
    [InlineData("jina")]
    [InlineData("firecrawl")]
    [InlineData("exa")]
    public async Task Payment_required_is_a_quota_error(string kind)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($$"""{ "error": "no credit {{Secret}}" }""", HttpStatusCode.PaymentRequired));
        var error = await Assert.ThrowsAsync<QuotaExhaustedException>(() => Read(Create(kind, handler)));

        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_server_error_is_a_typed_failure_with_no_server_text(string kind)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "error": "page text secret" }""", HttpStatusCode.InternalServerError));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create(kind, handler)));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jina")]
    [InlineData("firecrawl")]
    [InlineData("tavily")]
    [InlineData("parallel")]
    [InlineData("exa")]
    public async Task A_body_that_is_not_json_is_a_typed_failure(string kind)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text("<html>page text secret</html>", "text/html"));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create(kind, handler)));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Private_and_local_addresses_are_refused_before_any_request(string kind)
    {
        foreach (var address in new[] { "http://127.0.0.1/x", "http://intranet.local/x", "http://[::1]/x", "http://10.0.0.5/x", "http://169.254.169.254/latest/meta-data/" })
        {
            var handler = new StubHttpHandler(_ => Ok(kind, "body"));
            var reader = Create(kind, handler, resolve: (_, _) => throw new InvalidOperationException("no name lookup should be needed"));

            await Assert.ThrowsAsync<ServiceFailedException>(() => reader.ReadAsync(new Uri(address), 8000, CancellationToken.None));
            Assert.Empty(handler.Requests);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_refusal_names_the_reader_and_not_the_built_in_reader(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "body"));
        var reader = Create(kind, handler, resolve: (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.168.1.20") }));
        var addresses = new[] { "http://127.0.0.1/x", "http://intranet.local/x", "https://private.example/x", "file:///etc/passwd", "https://user:pass@site.example/x" };

        foreach (var address in addresses)
        {
            var error = await Assert.ThrowsAsync<ServiceFailedException>(() => reader.ReadAsync(new Uri(address), 8000, CancellationToken.None));
            Assert.Equal(reader.DisplayName, error.Service);
        }

        var unresolved = Create(kind, handler, resolve: (_, _) => throw new System.Net.Sockets.SocketException());
        var notFound = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(unresolved));
        Assert.Equal(unresolved.DisplayName, notFound.Service);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_name_that_resolves_to_a_private_address_is_refused_before_any_request(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "body"));
        var reader = Create(kind, handler, resolve: (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.168.1.20") }));

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Read(reader));

        Assert.DoesNotContain("192.168", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Only_http_and_https_addresses_are_sent(string kind)
    {
        var handler = new StubHttpHandler(_ => Ok(kind, "body"));

        await Assert.ThrowsAsync<ServiceFailedException>(() => Create(kind, handler).ReadAsync(new Uri("file:///etc/passwd"), 8000, CancellationToken.None));
        await Assert.ThrowsAsync<ServiceFailedException>(() => Create(kind, handler).ReadAsync(new Uri("https://user:pass@site.example/x"), 8000, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    // A body with no declared length must be stopped by the counted read before HttpClient buffers all of it, and the response must still be disposed.
    [Theory]
    [InlineData("jina")]
    [InlineData("firecrawl")]
    [InlineData("tavily")]
    [InlineData("parallel")]
    [InlineData("exa")]
    public async Task A_json_body_with_no_length_is_cut_off_at_the_cap_and_the_response_is_disposed(string kind)
    {
        var body = new CountingStream(64 * 1024 * 1024);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });

        await Assert.ThrowsAsync<ServiceFailedException>(() => Read(Create(kind, handler)));

        Assert.True(body.BytesRead <= (16 * 1024 * 1024) + (256 * 1024), $"read {body.BytesRead} bytes");
        Assert.True(body.Disposed);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_response_is_disposed_after_a_good_read(string kind)
    {
        var json = kind switch
        {
            "jina" => """{ "data": { "title": "T", "content": "body" } }""",
            "firecrawl" => """{ "success": true, "data": { "markdown": "body" } }""",
            "tavily" => """{ "results": [ { "url": "https://site.example/", "raw_content": "body" } ] }""",
            "brightdata" => "body",
            "parallel" => """{ "results": [ { "full_content": "body" } ] }""",
            _ => """{ "results": [ { "text": "body" } ] }""",
        };
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var stream = new TrackedStream(bytes);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });

        await Read(Create(kind, handler));

        Assert.True(stream.Disposed);
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
