using Gannet.Core.Services;
using Gannet.Providers.Http;
using Polly.Timeout;

namespace Gannet.Providers.Tests.Http;

public class ServiceHttpTests
{
    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw error;
    }

    private static TimeoutRejectedException Timeout() => new("The operation didn't complete within the allowed timeout of '00:00:20'.");

    [Fact]
    public async Task A_resilience_timeout_becomes_a_service_failure_naming_the_service()
    {
        using var http = new HttpClient(new ThrowingHandler(Timeout()));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/x");

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() =>
            ServiceHttp.SendCheckedAsync(http, request, "Tavily", (_, _) => Task.FromResult(false), CancellationToken.None));

        Assert.Equal("Tavily timed out", error.Message);
        Assert.Equal("Tavily", error.Service);
    }

    [Fact]
    public async Task The_plain_send_maps_the_timeout_too()
    {
        using var http = new HttpClient(new ThrowingHandler(Timeout()));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/x");

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() =>
            ServiceHttp.SendAsync(http, request, "Built-in reader", CancellationToken.None));

        Assert.Equal("Built-in reader timed out", error.Message);
    }

    [Fact]
    public async Task An_open_circuit_becomes_a_service_failure_in_gannets_words()
    {
        using var http = new HttpClient(new ThrowingHandler(new Polly.CircuitBreaker.BrokenCircuitException("The circuit is now open and is not allowing calls.")));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/x");

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => ServiceHttp.SendAsync(http, request, "Tavily", CancellationToken.None));

        Assert.Equal("Tavily is failing repeatedly; gannet is pausing calls to it", error.Message);
    }

    [Fact]
    public async Task A_rate_limiter_rejection_becomes_a_service_failure_in_gannets_words()
    {
        using var http = new HttpClient(new ThrowingHandler(new Polly.RateLimiting.RateLimiterRejectedException(TimeSpan.FromSeconds(1))));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/x");

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => ServiceHttp.SendAsync(http, request, "Tavily", CancellationToken.None));

        Assert.Equal("Tavily is being called too fast; gannet is slowing down", error.Message);
    }

    [Fact]
    public async Task A_connection_failure_is_left_alone()
    {
        using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("reset")));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/x");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            ServiceHttp.SendAsync(http, request, "Tavily", CancellationToken.None));
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_mapped()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var http = new HttpClient(new ThrowingHandler(new OperationCanceledException(cts.Token)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/x");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ServiceHttp.SendAsync(http, request, "Tavily", cts.Token));
    }

    [Fact]
    public async Task The_builtin_reader_maps_the_timeout()
    {
        using var http = new HttpClient(new ThrowingHandler(Timeout()));
        var reader = new Gannet.Providers.Readers.BuiltinReader(http, false, (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }), null);

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => reader.ReadAsync(new Uri("https://site.example/a"), 500, CancellationToken.None));

        Assert.Equal("Built-in reader timed out", error.Message);
    }

    [Fact]
    public async Task The_duckduckgo_search_maps_the_timeout()
    {
        using var http = new HttpClient(new ThrowingHandler(Timeout()));
        var search = new Gannet.Providers.Search.DuckDuckGoSearch(http, "duckduckgo", new Gannet.Core.Configuration.ServiceOptions(), TimeProvider.System, TimeSpan.Zero);

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => search.SearchAsync("q", 3, CancellationToken.None));

        Assert.Equal("DuckDuckGo (last resort) timed out", error.Message);
    }
}
