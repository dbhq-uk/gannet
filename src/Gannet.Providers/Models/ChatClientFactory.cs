using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Anthropic;
using Gannet.Core.Configuration;
using Gannet.Core.Models;
using Gannet.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace Gannet.Providers.Models;

internal sealed class ChatClientFactory(IOptions<GannetOptions> options, IHttpClientFactory http) : IChatClientFactory
{
    public const int OllamaDefaultContextCap = 32_768;
    private const int UnknownModelContext = 8192;
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";
    private const string DeepSeekEndpoint = "https://api.deepseek.com";
    private static readonly string[] Providers = ["Ollama", "OpenAI", "OpenAICompatible", "AzureOpenAI", "Anthropic", "Gemini", "OpenRouter", "DeepSeek"];

    private readonly ConcurrentDictionary<string, Task<ModelHandle>> cache = new(StringComparer.OrdinalIgnoreCase);

    // Names whose handle used fallback values because OpenRouter did not answer; the handle is returned but not kept, so the next call asks again.
    private readonly ConcurrentDictionary<string, bool> unanswered = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ModelDescriptor> Models =>
        [.. options.Value.Models.Select(m => new ModelDescriptor(m.Key, m.Value.Provider, m.Value.Model))];

    public async Task<ModelHandle> GetAsync(string name, CancellationToken ct)
    {
        if (!options.Value.Models.TryGetValue(name, out var model))
        {
            throw new UnknownServiceException("model", name, options.Value.Models.Keys);
        }

        // One probe is shared by every caller, so it must not stop when the first caller gives up; each caller only stops waiting.
        var task = cache.GetOrAdd(name, _ => CreateAsync(name, model, CancellationToken.None));
        try
        {
            var handle = await task.WaitAsync(ct);
            if (unanswered.TryRemove(name, out _))
            {
                cache.TryRemove(new KeyValuePair<string, Task<ModelHandle>>(name, task));
            }

            return handle;
        }
        catch when (task.IsFaulted)
        {
            // Do not cache a failure: a fixed configuration or a restarted Ollama should work without a restart.
            cache.TryRemove(new KeyValuePair<string, Task<ModelHandle>>(name, task));
            throw;
        }
    }

    /// <summary>Sets Ollama's <c>num_ctx</c> on every request, on a copy, so the caller's own options are never changed.</summary>
    internal static IChatClient WithNumCtx(IChatClient client, int context) =>
        client.AsBuilder()
            .ConfigureOptions(o =>
            {
                o.AdditionalProperties ??= [];
                o.AdditionalProperties["num_ctx"] = context;
            })
            .Build();

    private async Task<ModelHandle> CreateAsync(string name, ModelOptions model, CancellationToken ct)
    {
        switch (model.Provider.ToLowerInvariant())
        {
            case "ollama":
                {
                    var endpoint = new Uri(model.Endpoint ?? "http://localhost:11434");
                    var (trained, tools) = await OllamaModelInfo.ReadAsync(http.CreateClient(HttpClientNames.Ollama), endpoint, model.Model, ct);
                    var context = model.ContextWindow ?? Math.Min(trained, OllamaDefaultContextCap);
                    var client = WithNumCtx(new OllamaApiClient(OllamaHttp(endpoint), model.Model), context);
                    return new ModelHandle(name, client, new ModelCapabilities(context, model.SupportsTools ?? tools));
                }

            case "openai":
                return Handle(name, model, OpenAi(RequireKey(name, model), null).GetChatClient(model.Model).AsIChatClient());

            case "openaicompatible":
                {
                    var endpoint = model.Endpoint ?? throw new InvalidOperationException($"Model '{name}' needs an Endpoint, such as http://localhost:8080/v1.");
                    var client = OpenAi(string.IsNullOrWhiteSpace(model.ApiKey) ? "not-needed" : model.ApiKey, endpoint).GetChatClient(model.Model).AsIChatClient();

                    // Ask a vLLM or llama.cpp server for the context it is running with, unless configuration already says.
                    var served = model.ContextWindow is null
                        ? await OpenAiCompatibleInfo.ReadContextAsync(http.CreateClient(HttpClientNames.ModelServer), new Uri(endpoint), model.Model, ct, model.ApiKey)
                        : null;
                    return Handle(name, model, client, served);
                }

            case "azureopenai":
                {
                    var endpoint = model.Endpoint ?? throw new InvalidOperationException($"Model '{name}' needs the Azure endpoint ending /openai/v1/.");
                    return Handle(name, model, OpenAi(RequireKey(name, model), endpoint).GetChatClient(model.Model).AsIChatClient());
                }

            case "anthropic":
                {
                    // Claude families that predate adaptive thinking reject thinking.type=adaptive, so the table says which form each family takes.
                    var anthropic = new AnthropicClient { ApiKey = RequireKey(name, model), HttpClient = http.CreateClient(HttpClientNames.Anthropic) };
                    var thinking = ModelCapabilityTable.UsesExtendedThinking(model.Model) ? AnthropicThinkingMode.Extended : AnthropicThinkingMode.Adaptive;
                    return Handle(name, model, anthropic.AsIChatClient(model.Model, model.MaxOutputTokens, thinking));
                }

            case "gemini":
                {
                    var gemini = new Google.GenAI.Client(apiKey: RequireKey(name, model));
                    return Handle(name, model, gemini.AsIChatClient(model.Model));
                }

            case "openrouter":
                return await OpenRouterAsync(name, model, ct);

            case "deepseek":
                {
                    // DeepSeek's windows and tool support come from its own model table; an ID the table does not list gets the fixed fallback.
                    var client = OpenAiChat(RequireKey(name, model), model.Endpoint ?? DeepSeekEndpoint, null).GetChatClient(model.Model).AsIChatClient();
                    return Handle(name, model, client);
                }

            default:
                throw new InvalidOperationException($"Model '{name}' has provider '{model.Provider}'. Use one of: {string.Join(", ", Providers)}.");
        }
    }

    /// <summary>
    /// A client for chat, from the pooled named client: its base address is the endpoint ending in "/", so a path prefix on a reverse proxy is kept, and it has no timeout of
    /// its own, because a non-streaming generation sends no headers until it finishes. The run's cancellation bounds it.
    /// </summary>
    private HttpClient OllamaHttp(Uri endpoint)
    {
        var client = http.CreateClient(HttpClientNames.Ollama);
        client.BaseAddress = ModelUrl.WithTrailingSlash(endpoint);
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>The window is, in order: configuration, what a local server reported, the table of documented windows, then 8,192 with no tools (fixed mode). Tools: configuration, then the table unless a server answered.</summary>
    private static ModelHandle Handle(string name, ModelOptions model, IChatClient client, int? served = null)
    {
        var known = ModelCapabilityTable.Lookup(model.Model);

        // A server that answered reports a context but not tools: those come from configuration alone (vLLM and llama.cpp need flags for them).
        var capabilities = new ModelCapabilities(
            model.ContextWindow ?? served ?? known?.ContextWindow ?? UnknownModelContext,
            model.SupportsTools ?? (served is null ? known?.SupportsTools : null) ?? false);
        return new ModelHandle(name, client, capabilities);
    }

    /// <summary>
    /// OpenRouter's own catalogue says what each model can do, so the window and tools are, in order: configuration, what OpenRouter reports, then 8,192 with no tools. The static
    /// table is not used, because OpenRouter IDs ("anthropic/claude-...") are not the vendors' own. A probe that fails is not kept.
    /// </summary>
    private async Task<ModelHandle> OpenRouterAsync(string name, ModelOptions model, CancellationToken ct)
    {
        var key = RequireKey(name, model);
        var endpoint = model.Endpoint ?? OpenRouterEndpoint;
        var client = OpenAiChat(key, endpoint, new HeaderPolicy(new Dictionary<string, string>
        {
            ["HTTP-Referer"] = "https://github.com/dbhq-uk/gannet",
            ["X-OpenRouter-Title"] = "gannet",
        })).GetChatClient(model.Model).AsIChatClient();

        (int? ContextWindow, bool SupportsTools)? reported = null;
        if (model.ContextWindow is null || model.SupportsTools is null)
        {
            reported = await OpenRouterModelInfo.ReadAsync(http.CreateClient(HttpClientNames.ModelServer), new Uri(endpoint), model.Model, key, ct);
            if (reported is not { ContextWindow: not null })
            {
                unanswered[name] = true;
            }
        }

        return new ModelHandle(
            name,
            client,
            new ModelCapabilities(
                model.ContextWindow ?? reported?.ContextWindow ?? UnknownModelContext,
                model.SupportsTools ?? reported?.SupportsTools ?? false));
    }

    /// <summary>An OpenAI-format client whose requests go through the pooled chat HttpClient, which has no timeout of its own, and whose SDK timeout is off for the same reason.</summary>
    private OpenAIClient OpenAiChat(string key, string endpoint, PipelinePolicy? policy)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            Transport = new HttpClientPipelineTransport(http.CreateClient(HttpClientNames.ModelChat)),
            NetworkTimeout = Timeout.InfiniteTimeSpan,
        };
        if (policy is not null)
        {
            options.AddPolicy(policy, PipelinePosition.PerCall);
        }

        return new OpenAIClient(new ApiKeyCredential(key), options);
    }

    private static OpenAIClient OpenAi(string key, string? endpoint) =>
        new(new ApiKeyCredential(key), endpoint is null ? new OpenAIClientOptions() : new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

    private static string RequireKey(string name, ModelOptions model) =>
        string.IsNullOrWhiteSpace(model.ApiKey)
            ? throw new AuthRequiredException(model.Provider, $"Model '{name}' needs an API key in configuration (Gannet:Models:{name}:ApiKey).")
            : model.ApiKey;
}
