using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class SearxngSearchTests
{
    // Hand-written from the documented result fields; not a captured response.
    private const string Fixture = """
        { "query": "q", "results": [
            { "title": "One", "url": "https://one.example/", "content": "one snippet", "engine": "brave", "publishedDate": null },
            { "title": "Two", "url": "https://two.example/", "content": "two snippet", "engine": "duckduckgo" },
            { "title": "Three", "url": "https://three.example/", "content": "three snippet" }
        ] }
        """;

    [Fact]
    public async Task Queries_json_format_and_takes_max_results()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var searx = new SearxngSearch(handler.Client(), "searxng", new ServiceOptions { Endpoint = "http://localhost:8888/" });

        var hits = await searx.SearchAsync("dotnet agents", 2, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:8888/search?q=dotnet%20agents&format=json&categories=general", request.Uri.AbsoluteUri);
        Assert.Equal(["One", "Two"], hits.Select(h => h.Title));
        Assert.False(searx.ReturnsPageContent);
    }

    [Fact]
    public async Task Maps_snippet_and_published_date()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "results": [ { "title": "One", "url": "https://one.example/", "content": "one snippet", "publishedDate": "2026-10-02T09:30:00" }, { "title": "Odd", "url": "ftp://odd.example/" } ] }
            """));
        var hits = await new SearxngSearch(handler.Client(), "searxng", new ServiceOptions { Endpoint = "http://localhost:8888" }).SearchAsync("q", 5, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("one snippet", hit.Snippet);
        Assert.Null(hit.Content);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 9, 30, 0, TimeSpan.Zero), hit.Published);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refusal_explains_the_json_setting_and_any_proxy(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => new SearxngSearch(handler.Client(), "searxng", new ServiceOptions { Endpoint = "http://localhost:8888" }).SearchAsync("q", 3, CancellationToken.None));
        Assert.Contains("refused the request (401 or 403)", error.Message, StringComparison.Ordinal);
        Assert.Contains("json format in settings.yml", error.Message, StringComparison.Ordinal);
        Assert.Contains("proxy", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_urls_keep_the_first_hit_before_the_cap()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "results": [
                { "title": "First", "url": "https://dup.example/a", "content": "1" },
                { "title": "Again", "url": "https://dup.example/a", "content": "2" },
                { "title": "Other", "url": "https://other.example/", "content": "3" }
            ] }
            """));
        var hits = await new SearxngSearch(handler.Client(), "searxng", new ServiceOptions { Endpoint = "http://localhost:8888" }).SearchAsync("q", 2, CancellationToken.None);

        Assert.Equal(["First", "Other"], hits.Select(h => h.Title));
    }

    [Fact]
    public async Task No_results_wanted_sends_no_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Fixture));
        var hits = await new SearxngSearch(handler.Client(), "searxng", new ServiceOptions { Endpoint = "http://localhost:8888" }).SearchAsync("q", 0, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Rate_limit_is_typed()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<RateLimitedException>(() => new SearxngSearch(handler.Client(), "searxng", new ServiceOptions { Endpoint = "http://localhost:8888" }).SearchAsync("q", 3, CancellationToken.None));
    }

    [Fact]
    public void Endpoint_is_required()
    {
        Assert.Throws<ArgumentException>(() => new SearxngSearch(new HttpClient(), "searxng", new ServiceOptions()));
    }
}
