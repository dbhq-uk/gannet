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

    // Cacheable is false when OpenRouter did not answer and the handle holds fallback values: it is returned once and then dropped, so the next call asks again.
    private readonly ConcurrentDictionary<string, Task<(ModelHandle Handle, bool Cacheable)>> cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ModelDescriptor> Models =>
        [.. options.Value.Models.Select(m => new ModelDescriptor(m.Key, m.Value.Provider, m.Value.Model))];

    public async Task<ModelHandle> GetAsync(string name, CancellationToken ct)
    {
        if (!options.Value.Models.TryGetValue(name, out var model))
        {
            throw new UnknownServiceException("model", name, options.Value.Models.Keys);
        }

        // One probe is shared by every caller, so it must not stop when the first caller gives up; each caller only stops waiting.
        var task = cache.GetOrAdd(name, _ => CreateEntryAsync(name, model, CancellationToken.None));
        try
        {
            var (handle, cacheable) = await task.WaitAsync(ct);
            if (!cacheable)
            {
                cache.TryRemove(new KeyValuePair<string, Task<(ModelHandle, bool)>>(name, task));
            }

            return handle;
        }
        catch when (task.IsFaulted)
        {
            // Do not cache a failure: a fixed configuration or a restarted Ollama should work without a restart.
            cache.TryRemove(new KeyValuePair<string, Task<(ModelHandle, bool)>>(name, task));
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

    private async Task<(ModelHandle Handle, bool Cacheable)> CreateEntryAsync(string name, ModelOptions model, CancellationToken ct) =>
        string.Equals(model.Provider, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            ? await OpenRouterAsync(name, model, ct)
            : (await CreateAsync(name, model, ct), true);

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
                return Handle(name, model, OpenAiChat(RequireKey(name, model), null, null).GetChatClient(model.Model).AsIChatClient());

            case "openaicompatible":
                {
                    var endpoint = model.Endpoint ?? throw new InvalidOperationException($"Model '{name}' needs an Endpoint, such as http://localhost:8080/v1.");
                    var client = OpenAiChat(string.IsNullOrWhiteSpace(model.ApiKey) ? "not-needed" : model.ApiKey, endpoint, null).GetChatClient(model.Model).AsIChatClient();

                    // Ask a vLLM or llama.cpp server for the context it is running with, unless configuration already says.
                    var served = model.ContextWindow is null
                        ? await OpenAiCompatibleInfo.ReadContextAsync(http.CreateClient(HttpClientNames.ModelServer), new Uri(endpoint), model.Model, ct, model.ApiKey)
                        : null;
                    return Handle(name, model, client, served);
                }

            case "azureopenai":
                {
                    var endpoint = model.Endpoint ?? throw new InvalidOperationException($"Model '{name}' needs the Azure endpoint ending /openai/v1/.");
                    return Handle(name, model, OpenAiChat(RequireKey(name, model), endpoint, null).GetChatClient(model.Model).AsIChatClient());
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

            case "deepseek":
                {
                    // DeepSeek's windows and tool support come from its own model table; an ID the table does not list gets the fixed fallback.
                    // Thinking is on by default there, and its reasoning_content must be sent back on every later request or the API answers 400
                    // (api-docs.deepseek.com/guides/thinking_mode). The chat adapter does not carry that field, so thinking is always switched off.
                    var client = OpenAiChat(RequireKey(name, model), model.Endpoint ?? DeepSeekEndpoint, new JsonBodyPolicy("thinking", """{"type":"disabled"}"""))
                        .GetChatClient(model.Model).AsIChatClient();
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
    private async Task<(ModelHandle Handle, bool Cacheable)> OpenRouterAsync(string name, ModelOptions model, CancellationToken ct)
    {
        var key = RequireKey(name, model);
        var endpoint = model.Endpoint ?? OpenRouterEndpoint;
        var client = OpenAiChat(key, endpoint, new HeaderPolicy(new Dictionary<string, string>
        {
            ["HTTP-Referer"] = "https://github.com/dbhq-uk/gannet",
            ["X-OpenRouter-Title"] = "gannet",
        })).GetChatClient(model.Model).AsIChatClient();

        (int? ContextWindow, bool SupportsTools)? reported = null;
        var needsProbe = model.ContextWindow is null || model.SupportsTools is null;
        if (needsProbe)
        {
            reported = await OpenRouterModelInfo.ReadAsync(http.CreateClient(HttpClientNames.ModelServer), new Uri(endpoint), model.Model, key, ct);
        }

        var handle = new ModelHandle(
            name,
            client,
            new ModelCapabilities(
                model.ContextWindow ?? reported?.ContextWindow ?? UnknownModelContext,
                model.SupportsTools ?? reported?.SupportsTools ?? false));

        // Nothing was asked, or OpenRouter answered with a window: keep it. An unanswered probe gives fallback values that must not stick.
        return (handle, !needsProbe || reported is { ContextWindow: not null });
    }

    /// <summary>An OpenAI-format client whose requests go through the pooled chat HttpClient, which has no timeout of its own, and whose SDK timeout is off for the same reason.</summary>
    private OpenAIClient OpenAiChat(string key, string? endpoint, PipelinePolicy? policy) =>
        new(new ApiKeyCredential(key), OpenAiChatOptions(endpoint, policy));

    /// <summary>
    /// The SDK would otherwise apply its own 100 s network timeout and retry a timeout four times, which cuts a long generation short. The transport is the pooled named client
    /// (no timeout of its own), so the run's cancellation is the only bound. A null <paramref name="endpoint"/> keeps the SDK's own default (OpenAI).
    /// </summary>
    internal OpenAIClientOptions OpenAiChatOptions(string? endpoint, PipelinePolicy? policy)
    {
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(http.CreateClient(HttpClientNames.ModelChat)),
            NetworkTimeout = Timeout.InfiniteTimeSpan,
        };
        if (endpoint is not null)
        {
            options.Endpoint = new Uri(endpoint);
        }

        if (policy is not null)
        {
            options.AddPolicy(policy, PipelinePosition.PerCall);
        }

        return options;
    }

    private static string RequireKey(string name, ModelOptions model) =>
        string.IsNullOrWhiteSpace(model.ApiKey)
            ? throw new AuthRequiredException(model.Provider, $"Model '{name}' needs an API key in configuration (Gannet:Models:{name}:ApiKey).")
            : model.ApiKey;
}
