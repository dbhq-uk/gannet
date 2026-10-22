using System.Net;
using System.Text.Json;
using Gannet.Core;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Gannet.Providers.Tests;

public class ServiceCatalogTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGannetCore(configuration);
        services.AddGannetProviders(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static IServiceCatalog Build(Dictionary<string, string?> settings) => BuildProvider(settings).GetRequiredService<IServiceCatalog>();

    [Fact]
    public void Zero_config_offers_keyless_tavily_builtin_and_jina()
    {
        var catalog = Build([]);
        Assert.Equal(["tavily"], catalog.SearchServices.Select(s => s.Name));
        Assert.Equal(["builtin", "jina"], catalog.Readers.Select(r => r.Name));
        Assert.Equal("tavily", catalog.GetSearch("tavily").Type);
    }

    [Fact]
    public void Configured_entries_use_their_type_or_their_name()
    {
        var catalog = Build(new()
        {
            ["Gannet:Search:brave-work:Type"] = "brave",
            ["Gannet:Search:brave-work:ApiKey"] = "k",
            ["Gannet:Search:searxng:Endpoint"] = "http://localhost:8888",
            ["Gannet:Readers:Services:brightdata:Type"] = "brightdata-unlocker",
            ["Gannet:Readers:Services:brightdata:ApiKey"] = "bd",
            ["Gannet:Readers:Services:brightdata:Zone"] = "z",
        });

        Assert.Equal(["brave-work", "searxng"], catalog.SearchServices.Select(s => s.Name).Order());
        Assert.Equal("brave", catalog.GetSearch("brave-work").Type);
        Assert.Equal("brightdata-unlocker", catalog.GetReader("brightdata").Type);
        Assert.Contains("builtin", catalog.Readers.Select(r => r.Name));
    }

    [Fact]
    public void Unknown_names_list_what_is_configured()
    {
        var catalog = Build([]);
        var error = Assert.Throws<UnknownServiceException>(() => catalog.GetSearch("bing"));
        Assert.Contains("tavily", error.Message, StringComparison.Ordinal);
        Assert.Throws<UnknownServiceException>(() => catalog.GetReader("nope"));
    }

    [Fact]
    public void Unknown_type_fails_at_start_with_the_valid_types()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(new() { ["Gannet:Search:x:Type"] = "bing" }));
        Assert.Contains("tavily", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptors_never_carry_keys()
    {
        var catalog = Build(new() { ["Gannet:Search:serper:ApiKey"] = "super-secret" });
        Assert.DoesNotContain("super-secret", JsonSerializer.Serialize(catalog.SearchServices), StringComparison.Ordinal);
    }

    [Fact]
    public void Budget_binds_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gannet:Budget:MaxRounds"] = "0" }).Build();
        var services = new ServiceCollection().AddGannetCore(configuration).BuildServiceProvider();
        Assert.Equal(0, services.GetRequiredService<IOptions<GannetOptions>>().Value.Budget.MaxRounds);
    }

    [Fact]
    public void Every_search_type_and_reader_type_can_be_built()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gannet:Search:searxng:Endpoint"] = "http://localhost:8888",
            ["Gannet:Search:parallel:ApiKey"] = "k",
            ["Gannet:Readers:Services:brightdata-unlocker:ApiKey"] = "k",
            ["Gannet:Readers:Services:brightdata-unlocker:Zone"] = "z",
            ["Gannet:Readers:Services:parallel-extract:ApiKey"] = "k",
        };
        foreach (var type in new[] { "tavily", "brave", "serper", "firecrawl", "exa", "duckduckgo" })
        {
            settings[$"Gannet:Search:{type}:ApiKey"] = "k";
        }

        settings["Gannet:Search:brightdata:ApiKey"] = "k";
        settings["Gannet:Search:brightdata:Zone"] = "z";
        foreach (var type in new[] { "jina", "firecrawl", "tavily-extract", "exa-contents" })
        {
            settings[$"Gannet:Readers:Services:{type}:ApiKey"] = "k";
        }

        var catalog = Build(settings);

        Assert.Equal(9, catalog.SearchServices.Count);
        Assert.Equal(7, catalog.Readers.Count);
        Assert.Equal(
            ["brave", "brightdata", "duckduckgo", "exa", "firecrawl", "parallel", "searxng", "serper", "tavily"],
            catalog.SearchServices.Select(s => s.Type).Order());
        Assert.Equal(
            ["brightdata-unlocker", "builtin", "exa-contents", "firecrawl", "jina", "parallel-extract", "tavily-extract"],
            catalog.Readers.Select(r => r.Type).Order());
    }

    [Fact]
    public void The_builtin_reader_gets_the_private_address_option()
    {
        var allowed = Build(new() { ["Gannet:Readers:AllowPrivateAddresses"] = "true" });
        var refused = Build([]);

        Assert.True(PrivateAddressesAllowed(allowed.GetReader("builtin")));
        Assert.False(PrivateAddressesAllowed(refused.GetReader("builtin")));
    }

    private static bool PrivateAddressesAllowed(IPageReader reader) =>
        (bool)typeof(Gannet.Providers.Readers.BuiltinReader).GetField("allowPrivateAddresses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(reader)!;

    [Fact]
    public void A_remote_reader_missing_its_zone_fails_at_start_without_printing_the_key()
    {
        var error = Assert.Throws<ArgumentException>(() => Build(new()
        {
            ["Gannet:Readers:Services:brightdata:Type"] = "brightdata-unlocker",
            ["Gannet:Readers:Services:brightdata:ApiKey"] = "bd-secret-key",
        }));
        Assert.Contains("Zone", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bd-secret-key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void S3_kind_replaces_the_blob_store()
    {
        using var provider = BuildProvider(new() { ["Gannet:Storage:Kind"] = "S3" });
        Assert.Throws<NotSupportedException>(() => provider.GetRequiredService<Gannet.Core.Storage.IBlobStore>());
    }

    [Fact]
    public void Local_kind_keeps_the_local_blob_store()
    {
        using var provider = BuildProvider(new() { ["Gannet:Storage:Path"] = Path.Combine(Path.GetTempPath(), "gannet-catalog-test") });
        Assert.IsType<Gannet.Core.Storage.LocalDiskBlobStore>(provider.GetRequiredService<Gannet.Core.Storage.IBlobStore>());
    }

    [Fact]
    public async Task The_duckduckgo_request_carries_exactly_one_user_agent()
    {
        var stub = new StubHttpHandler(_ => StubHttpHandler.Text("<html><body></body></html>"));
        using var provider = BuildProvider(
            new() { ["Gannet:Search:duckduckgo:ApiKey"] = "unused" },
            s => s.AddHttpClient(HttpClientNames.Search("duckduckgo")).ConfigurePrimaryHttpMessageHandler(() => stub));

        await provider.GetRequiredService<IServiceCatalog>().GetSearch("duckduckgo").SearchAsync("q", 3, CancellationToken.None);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("gannet/1.0 (+https://github.com/dbhq-uk/gannet)", request.Headers["User-Agent"]);
    }

    [Fact]
    public async Task Other_search_clients_send_the_gannet_user_agent()
    {
        var stub = new StubHttpHandler(_ => StubHttpHandler.Json("{\"results\":[]}"));
        using var provider = BuildProvider(
            new() { ["Gannet:Search:tavily:ApiKey"] = "k" },
            s => s.AddHttpClient(HttpClientNames.Search("tavily")).ConfigurePrimaryHttpMessageHandler(() => stub));

        await provider.GetRequiredService<IServiceCatalog>().GetSearch("tavily").SearchAsync("q", 3, CancellationToken.None);

        Assert.Contains("gannet/1.0", Assert.Single(stub.Requests).Headers["User-Agent"], StringComparison.Ordinal);
    }

    private static Action<IServiceCollection> Stub(string client, HttpMessageHandler handler, bool fastRetries = true) => services =>
    {
        services.AddHttpClient(client).ConfigurePrimaryHttpMessageHandler(() => handler);
        if (fastRetries && client.StartsWith("gannet-search-", StringComparison.Ordinal))
        {
            services.Configure<HttpStandardResilienceOptions>($"{client}-standard", o =>
            {
                o.Retry.Delay = TimeSpan.FromMilliseconds(1);
                o.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                o.Retry.UseJitter = false;
            });
        }
    };

    private sealed class CountingHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int count;

        public int Count => count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, Interlocked.Increment(ref count), cancellationToken);
    }

    [Fact]
    public async Task A_paid_post_that_times_out_is_not_sent_twice()
    {
        var handler = new CountingHandler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var provider = BuildProvider(
            new() { ["Gannet:Search:tavily:ApiKey"] = "k" },
            s =>
            {
                Stub("gannet-search-tavily", handler)(s);
                s.Configure<HttpStandardResilienceOptions>("gannet-search-tavily-standard", o => o.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(200));
            });

        await Assert.ThrowsAsync<ServiceFailedException>(() => provider.GetRequiredService<IServiceCatalog>().GetSearch("tavily").SearchAsync("q", 3, CancellationToken.None));

        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task A_paid_post_that_fails_to_connect_is_not_sent_twice()
    {
        var handler = new CountingHandler((_, _, _) => throw new HttpRequestException("connection reset"));
        using var provider = BuildProvider(new() { ["Gannet:Search:tavily:ApiKey"] = "k" }, Stub("gannet-search-tavily", handler));

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetRequiredService<IServiceCatalog>().GetSearch("tavily").SearchAsync("q", 3, CancellationToken.None));

        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_post_is_retried_after_429_and_5xx(HttpStatusCode status)
    {
        var handler = new CountingHandler((_, n, _) => Task.FromResult(n < 3
            ? new HttpResponseMessage(status)
            : StubHttpHandler.Json("{\"results\":[]}")));
        using var provider = BuildProvider(new() { ["Gannet:Search:tavily:ApiKey"] = "k" }, Stub("gannet-search-tavily", handler));

        await provider.GetRequiredService<IServiceCatalog>().GetSearch("tavily").SearchAsync("q", 3, CancellationToken.None);

        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task A_get_is_retried_after_a_connection_failure_on_the_service_client()
    {
        var handler = new CountingHandler((_, n, _) => n == 1
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(StubHttpHandler.Json("{\"data\":{\"title\":\"T\",\"content\":\"Page text for the reader\"}}")));
        using var provider = BuildProvider(
            new() { ["Gannet:Readers:Services:jina:ApiKey"] = "k" },
            Stub(HttpClientNames.Service, handler));
        var jina = provider.GetRequiredService<IServiceCatalog>().GetReader("jina");

        var page = await jina.ReadAsync(new Uri("https://93.184.216.34/a"), 500, CancellationToken.None);

        Assert.Equal(2, handler.Count);
        Assert.Contains("Page text", page.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_post_to_a_remote_reader_that_fails_to_connect_is_not_sent_twice()
    {
        var handler = new CountingHandler((_, _, _) => throw new HttpRequestException("connection reset"));
        using var provider = BuildProvider(
            new() { ["Gannet:Readers:Services:firecrawl:ApiKey"] = "k" },
            Stub(HttpClientNames.Service, handler));
        var reader = provider.GetRequiredService<IServiceCatalog>().GetReader("firecrawl");

        await Assert.ThrowsAnyAsync<Exception>(() => reader.ReadAsync(new Uri("https://93.184.216.34/a"), 500, CancellationToken.None));

        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Check_reports_an_unexpected_exception_by_type_name_only()
    {
        var handler = new CountingHandler((_, _, _) => throw new InvalidOperationException("SECRET-SERVER-TEXT"));
        using var provider = BuildProvider(new() { ["Gannet:Search:tavily:ApiKey"] = "k" }, Stub("gannet-search-tavily", handler));

        var result = await provider.GetRequiredService<IServiceCatalog>().CheckAsync("search", "tavily", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(nameof(InvalidOperationException), result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_reports_a_timeout_that_is_not_the_callers_as_a_failed_check()
    {
        var handler = new CountingHandler((_, _, _) => throw new TaskCanceledException("timed out"));
        using var provider = BuildProvider(new() { ["Gannet:Search:tavily:ApiKey"] = "k" }, Stub("gannet-search-tavily", handler));

        var result = await provider.GetRequiredService<IServiceCatalog>().CheckAsync("search", "tavily", CancellationToken.None);

        Assert.Equal(new ServiceCheckResult(false, "Timed out."), result);
    }

    [Fact]
    public async Task Check_does_not_swallow_the_callers_cancellation()
    {
        var handler = new CountingHandler((_, _, ct) => throw new OperationCanceledException(ct));
        using var provider = BuildProvider(new() { ["Gannet:Search:tavily:ApiKey"] = "k" }, Stub("gannet-search-tavily", handler));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<IServiceCatalog>().CheckAsync("search", "tavily", cts.Token));
    }

    [Fact]
    public async Task Check_still_rejects_an_unknown_kind_as_a_programming_error()
    {
        using var provider = BuildProvider([]);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<IServiceCatalog>().CheckAsync("nope", "x", CancellationToken.None));
    }
}
