using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Core.Services;
using Gannet.Core.Storage;
using Gannet.Providers.Http;
using Gannet.Providers.Models;
using Gannet.Providers.Readers;
using Gannet.Providers.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using DiDescriptor = Microsoft.Extensions.DependencyInjection.ServiceDescriptor;

namespace Gannet.Providers;

public static class ProvidersServiceCollectionExtensions
{
    public static IServiceCollection AddGannetProviders(this IServiceCollection services, IConfiguration configuration)
    {
        // One client per search type, so one failing service cannot open the circuit for another.
        foreach (var type in ServiceCatalog.SearchTypes.Keys)
        {
            var builder = services.AddHttpClient(HttpClientNames.Search(type), c =>
            {
                // DuckDuckGo sets its own User-Agent on the request, and HttpClient would ignore this default for that request. Left off so there is one source of truth.
                if (!string.Equals(type, "duckduckgo", StringComparison.OrdinalIgnoreCase))
                {
                    c.DefaultRequestHeaders.UserAgent.ParseAdd(BuiltinReader.UserAgent);
                }
            });
            builder.AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                o.Retry.MaxRetryAttempts = 2;
                o.Retry.ShouldHandle = RetryRules.ShouldRetry;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60); // at least twice the attempt timeout
            });
        }

        // Remote readers render pages, so they are slower; no circuit breaker shared across sites.
        services.AddHttpClient(HttpClientNames.Service, c => c.DefaultRequestHeaders.UserAgent.ParseAdd(BuiltinReader.UserAgent))
            .AddResilienceHandler("gannet-service", b =>
            {
                b.AddTimeout(TimeSpan.FromSeconds(90));
                b.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 1, ShouldHandle = RetryRules.ShouldRetry });
                b.AddTimeout(TimeSpan.FromSeconds(40));
            });

        // The built-in reader talks to arbitrary websites, optionally through the configured proxy.
        services.AddHttpClient(HttpClientNames.Reader)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var readers = sp.GetRequiredService<IOptions<GannetOptions>>().Value.Readers;
                var proxy = readers.Proxy;

                // One definition for both uses below: a blank URL is no proxy, so the connect guard must stay on.
                var useProxy = !string.IsNullOrWhiteSpace(proxy?.Url);
                var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,

                    // BuiltinReader follows redirects itself and checks each hop (spec s.5, public internet only).
                    AllowAutoRedirect = false,

                    // Rebinding defence when there is no proxy: the connection is refused unless the address is public.
                    ConnectCallback = useProxy || readers.AllowPrivateAddresses ? null : PublicAddressGuard.ConnectAsync,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };
                if (useProxy)
                {
                    handler.Proxy = new WebProxy(new Uri(proxy!.Url!))
                    {
                        Credentials = proxy.Username is null ? null : new NetworkCredential(proxy.Username, proxy.Password),
                    };
                    handler.UseProxy = true;
                }
                else
                {
                    // Without this the handler picks up HTTP_PROXY from the environment, and the connect check then vets the proxy, not the site.
                    handler.UseProxy = false;
                }

                return handler;
            })
            .AddResilienceHandler("gannet-reader", b =>
            {
                b.AddTimeout(TimeSpan.FromSeconds(45));
                b.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 1, ShouldHandle = RetryRules.ShouldRetry });
                b.AddTimeout(TimeSpan.FromSeconds(20));
            });

        // No timeout of its own: a non-streaming Ollama generation sends no headers until it finishes, so only the run's cancellation bounds it. The model probe sets its own deadline.
        services.AddHttpClient(HttpClientNames.Ollama, c => c.Timeout = Timeout.InfiniteTimeSpan);

        // The Anthropic SDK applies its own timeout and retries; HttpClient's 100 s default would cut a long thinking response short.
        services.AddHttpClient(HttpClientNames.Anthropic, c => c.Timeout = Timeout.InfiniteTimeSpan);

        // Probes a local vLLM or llama.cpp server for its context length; a server that does not answer quickly is treated as not answering.
        services.AddHttpClient(HttpClientNames.ModelServer, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IServiceCatalog, ServiceCatalog>();

        if (string.Equals(configuration.GetSection(GannetOptions.Section)["Storage:Kind"], "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.Replace(DiDescriptor.Singleton<IBlobStore>(sp => S3BlobStoreFactory.Create(sp.GetRequiredService<IOptions<GannetOptions>>().Value.Storage.S3)));
        }

        return services;
    }
}
