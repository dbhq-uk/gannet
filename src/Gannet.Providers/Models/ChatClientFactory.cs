using System.ClientModel;
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
    private static readonly string[] Providers = ["Ollama", "OpenAI", "OpenAICompatible", "AzureOpenAI", "Anthropic", "Gemini"];

    private readonly ConcurrentDictionary<string, Task<ModelHandle>> cache = new(StringComparer.OrdinalIgnoreCase);

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
            return await task.WaitAsync(ct);
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
                    var client = WithNumCtx(new OllamaApiClient(endpoint, model.Model), context);
                    return new ModelHandle(name, client, new ModelCapabilities(context, model.SupportsTools ?? tools));
                }

            case "openai":
                return Handle(name, model, OpenAi(RequireKey(name, model), null).GetChatClient(model.Model).AsIChatClient());

            case "openaicompatible":
                {
                    var endpoint = model.Endpoint ?? throw new InvalidOperationException($"Model '{name}' needs an Endpoint, such as http://localhost:8080/v1.");
                    var client = OpenAi(model.ApiKey ?? "not-needed", endpoint).GetChatClient(model.Model).AsIChatClient();

                    // Ask a vLLM or llama.cpp server for the context it is running with, unless configuration already says.
                    var served = model.ContextWindow is null
                        ? await OpenAiCompatibleInfo.ReadContextAsync(http.CreateClient(HttpClientNames.ModelServer), new Uri(endpoint), model.Model, ct)
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
                    var anthropic = new AnthropicClient { ApiKey = RequireKey(name, model) };
                    return Handle(name, model, anthropic.AsIChatClient(model.Model, defaultMaxOutputTokens: model.MaxOutputTokens));
                }

            case "gemini":
                {
                    var gemini = new Google.GenAI.Client(apiKey: RequireKey(name, model));
                    return Handle(name, model, gemini.AsIChatClient(model.Model));
                }

            default:
                throw new InvalidOperationException($"Model '{name}' has provider '{model.Provider}'. Use one of: {string.Join(", ", Providers)}.");
        }
    }

    /// <summary>The window is, in order: configuration, what a local server reported, the table of documented windows, then 8,192 with no tools (fixed mode).</summary>
    private static ModelHandle Handle(string name, ModelOptions model, IChatClient client, int? served = null)
    {
        var known = ModelCapabilityTable.Lookup(model.Model);
        var capabilities = new ModelCapabilities(
            model.ContextWindow ?? served ?? known?.ContextWindow ?? UnknownModelContext,
            model.SupportsTools ?? known?.SupportsTools ?? false);
        return new ModelHandle(name, client, capabilities);
    }

    private static OpenAIClient OpenAi(string key, string? endpoint) =>
        new(new ApiKeyCredential(key), endpoint is null ? new OpenAIClientOptions() : new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

    private static string RequireKey(string name, ModelOptions model) =>
        string.IsNullOrWhiteSpace(model.ApiKey)
            ? throw new AuthRequiredException(model.Provider, $"Model '{name}' needs an API key in configuration (Gannet:Models:{name}:ApiKey).")
            : model.ApiKey;
}
