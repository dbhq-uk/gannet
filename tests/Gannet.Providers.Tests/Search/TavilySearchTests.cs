using System.Net;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class TavilySearchTests
{
    // Hand-written from the documented response shape; not a captured response.
    private const string Fixture = """
        { "query": "q", "results": [
            { "title": "Alpha", "url": "https://alpha.example/a", "content": "alpha snippet", "raw_content": "# Alpha\n\nfull text", "published_date": "2026-10-01" },
            { "title": "Bad", "url": "not a url", "content": "x" },
            { "title": "Beta", "url": "https://beta.example/b", "content": "beta snippet", "raw_content": null }
        ] }
        """;

    [Fact]
    public async Task Keyed_search_sends_bearer_and_maps_results()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var tavily = new TavilySearch(handler.Client(), "tavily", new ServiceOptions { ApiKey = "tvly-test" });

        var hits = await tavily.SearchAsync("q", 5, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.tavily.com/search", request.Uri.ToString());
        Assert.Equal("Bearer tvly-test", request.Headers["Authorization"]);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("q", body.RootElement.GetProperty("query").GetString());
        Assert.Equal(5, body.RootElement.GetProperty("max_results").GetInt32());
        Assert.Equal("basic", body.RootElement.GetProperty("search_depth").GetString());
        Assert.Equal("markdown", body.RootElement.GetProperty("include_raw_content").GetString());
        Assert.True(body.RootElement.GetProperty("include_published_date").GetBoolean());

        Assert.Equal(2, hits.Count);
        Assert.Equal("Alpha", hits[0].Title);
        Assert.Equal("alpha snippet", hits[0].Snippet);
        Assert.Equal("# Alpha\n\nfull text", hits[0].Content);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), hits[0].Published);
        Assert.Null(hits[1].Content);
        Assert.True(tavily.ReturnsPageContent);
    }

    [Fact]
    public async Task No_key_uses_keyless_mode()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await new TavilySearch(handler.Client(), "tavily", new ServiceOptions()).SearchAsync("q", 3, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("keyless", request.Headers["X-Tavily-Access-Mode"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Keyless_option_wins_over_a_key()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await new TavilySearch(handler.Client(), "tavily", new ServiceOptions { ApiKey = "tvly-test", Keyless = true }).SearchAsync("q", 3, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("keyless", request.Headers["X-Tavily-Access-Mode"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task The_key_is_never_in_the_url()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await new TavilySearch(handler.Client(), "tavily", new ServiceOptions { ApiKey = "tvly-secret" }).SearchAsync("q", 3, CancellationToken.None);

        Assert.DoesNotContain("tvly-secret", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 20)]
    public async Task Max_results_stays_inside_what_tavily_accepts(int asked, int sent)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        await new TavilySearch(handler.Client(), "tavily", new ServiceOptions()).SearchAsync("q", asked, CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal(sent, body.RootElement.GetProperty("max_results").GetInt32());
    }

    [Fact]
    public async Task Rate_limit_is_typed()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => new TavilySearch(handler.Client(), "tavily", new ServiceOptions()).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_key_is_typed_and_the_message_has_no_key_or_body()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "detail": "bad key tvly-secret" }""", HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => new TavilySearch(handler.Client(), "tavily", new ServiceOptions { ApiKey = "tvly-secret" }).SearchAsync("q", 3, CancellationToken.None));

        Assert.DoesNotContain("tvly-secret", error.Message, StringComparison.Ordinal);
    }
}
