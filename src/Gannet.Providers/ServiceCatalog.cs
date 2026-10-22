using System.Diagnostics;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Core.Services;
using Gannet.Providers.Http;
using Gannet.Providers.Readers;
using Gannet.Providers.Search;
using Microsoft.Extensions.Options;

namespace Gannet.Providers;

internal static class HttpClientNames
{
    public const string Reader = "gannet-reader";
    public const string Service = "gannet-service";
    public const string Ollama = "gannet-ollama";
    public const string Anthropic = "gannet-anthropic";
    public const string ModelServer = "gannet-model-server";
    public const string ModelChat = "gannet-model-chat";

    public static string Search(string type) => $"gannet-search-{type}";
}

internal sealed class ServiceCatalog : IServiceCatalog
{
    internal static readonly Dictionary<string, Func<HttpClient, string, ServiceOptions, ISearchProvider>> SearchTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tavily"] = (h, n, o) => new TavilySearch(h, n, o),
        ["searxng"] = (h, n, o) => new SearxngSearch(h, n, o),
        ["brave"] = (h, n, o) => new BraveSearch(h, n, o),
        ["serper"] = (h, n, o) => new SerperSearch(h, n, o),
        ["firecrawl"] = (h, n, o) => new FirecrawlSearch(h, n, o),
        ["parallel"] = (h, n, o) => new ParallelSearch(h, n, o),
        ["exa"] = (h, n, o) => new ExaSearch(h, n, o),
        ["brightdata"] = (h, n, o) => new BrightDataSearch(h, n, o),
        ["duckduckgo"] = (h, n, o) => new DuckDuckGoSearch(h, n, o),
    };

    internal static readonly Dictionary<string, Func<HttpClient, string, ServiceOptions, IPageReader>> ReaderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jina"] = (h, n, o) => new JinaReader(h, n, o),
        ["firecrawl"] = (h, n, o) => new FirecrawlScrapeReader(h, n, o),
        ["tavily-extract"] = (h, n, o) => new TavilyExtractReader(h, n, o),
        ["brightdata-unlocker"] = (h, n, o) => new BrightDataUnlockerReader(h, n, o),
        ["parallel-extract"] = (h, n, o) => new ParallelExtractReader(h, n, o),
        ["exa-contents"] = (h, n, o) => new ExaContentsReader(h, n, o),
    };

    private readonly IHttpClientFactory http;
    private readonly IChatClientFactory models;
    private readonly Dictionary<string, (string Type, ServiceOptions Options)> search;
    private readonly Dictionary<string, (string Type, ServiceOptions Options)> readers;
    private readonly bool allowPrivateAddresses;

    public ServiceCatalog(IOptions<GannetOptions> options, IHttpClientFactory http, IChatClientFactory models)
    {
        this.http = http;
        this.models = models;
        var gannet = options.Value;
        allowPrivateAddresses = gannet.Readers.AllowPrivateAddresses;
        ProxySettings.Validate(gannet.Readers.Proxy);

        search = Resolve(gannet.Search, SearchTypes.Keys, "search service");
        if (search.Count == 0)
        {
            search["tavily"] = ("tavily", new ServiceOptions { Keyless = true });
        }

        readers = Resolve(gannet.Readers.Services, ReaderTypes.Keys, "reader");
        readers.TryAdd("jina", ("jina", new ServiceOptions()));

        // Building every provider once at start-up surfaces configuration mistakes (a missing zone or endpoint) immediately.
        SearchServices = [.. search.Keys.Select(n => Describe(GetSearch(n)))];
        Readers = [Describe(GetReader("builtin")), .. readers.Keys.Select(n => Describe(GetReader(n)))];
    }

    public IReadOnlyList<ServiceDescriptor> SearchServices { get; }

    public IReadOnlyList<ServiceDescriptor> Readers { get; }

    public IReadOnlyList<ModelDescriptor> Models => models.Models;

    public ISearchProvider GetSearch(string name)
    {
        if (!search.TryGetValue(name, out var entry))
        {
            throw new UnknownServiceException("search service", name, search.Keys);
        }

        return SearchTypes[entry.Type](http.CreateClient(HttpClientNames.Search(entry.Type)), name, entry.Options);
    }

    public IPageReader GetReader(string name)
    {
        if (string.Equals(name, "builtin", StringComparison.OrdinalIgnoreCase))
        {
            // Pass the option through, or the pre-check still refuses private hosts (Task 16 review).
            return new BuiltinReader(http.CreateClient(HttpClientNames.Reader), allowPrivateAddresses);
        }

        if (!readers.TryGetValue(name, out var entry))
        {
            throw new UnknownServiceException("reader", name, ["builtin", .. readers.Keys]);
        }

        return ReaderTypes[entry.Type](http.CreateClient(HttpClientNames.Service), name, entry.Options);
    }

    public async Task<ServiceCheckResult> CheckAsync(string kind, string name, CancellationToken ct)
    {
        if (kind is not ("search" or "reader" or "model"))
        {
            throw new ArgumentException($"Unknown kind '{kind}'.", nameof(kind));
        }

        try
        {
            switch (kind)
            {
                case "search":
                    var hits = await GetSearch(name).SearchAsync("gannet connectivity check", 1, ct);
                    return new ServiceCheckResult(true, $"Answered with {hits.Count} result(s).");
                case "reader":
                    var page = await GetReader(name).ReadAsync(new Uri("https://example.com/"), 500, ct);
                    return new ServiceCheckResult(true, $"Read example.com ({page.Markdown.Length} characters).");
                case "model":
                    var model = await models.GetAsync(name, ct);
                    var mode = model.Capabilities.CanRunAgentMode ? "agent" : "fixed";
                    return new ServiceCheckResult(true, $"Reachable. Context {model.Capabilities.ContextWindow:N0} tokens; tools {(model.Capabilities.SupportsTools ? "yes" : "no")}; {mode} mode.");
                default:
                    throw new UnreachableException();
            }
        }
        catch (ServiceException e)
        {
            return new ServiceCheckResult(false, e.Message);
        }
        catch (HttpRequestException e)
        {
            return new ServiceCheckResult(false, $"Could not connect ({e.HttpRequestError}).");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ServiceCheckResult(false, "Timed out.");
        }
        catch (Exception e) when (e is not (OperationCanceledException or ArgumentException))
        {
            // A bug in a provider is a failed check, not a crash. Only the type name is shown: the message can carry server text.
            return new ServiceCheckResult(false, $"The check failed unexpectedly ({e.GetType().Name}).");
        }
    }

    private static Dictionary<string, (string Type, ServiceOptions Options)> Resolve(Dictionary<string, ServiceOptions> configured, IEnumerable<string> types, string kind)
    {
        var known = types.ToList();
        var result = new Dictionary<string, (string Type, ServiceOptions Options)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, options) in configured)
        {
            var type = options.Type ?? name;
            if (!known.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The {kind} '{name}' has type '{type}', which gannet does not know. Types: {string.Join(", ", known)}.");
            }

            result[name] = (type.ToLowerInvariant(), options);
        }

        return result;
    }

    private static ServiceDescriptor Describe(ISearchProvider p) => new(p.Name, p.Type, p.DisplayName, p.Terms, p.ReturnsPageContent);

    private static ServiceDescriptor Describe(IPageReader r) => new(r.Name, r.Type, r.DisplayName, r.Terms, true);
}
