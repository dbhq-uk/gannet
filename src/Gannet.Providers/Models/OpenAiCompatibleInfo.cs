using System.Net.Http.Headers;
using System.Text.Json;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Models;

/// <summary>
/// The context length a vLLM or llama.cpp server is running with. vLLM answers GET {endpoint}/models with data[].max_model_len; llama.cpp answers GET {server root}/props with
/// default_generation_settings.n_ctx (the allocated context per slot; /v1/models carries only the trained length, n_ctx_train).
/// </summary>
internal static class OpenAiCompatibleInfo
{
    private const string Service = "The model server";
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>
    /// The context length the server reports for <paramref name="model"/>, or null when it answers neither shape. Only the caller's own cancellation escapes.
    /// A configured <paramref name="apiKey"/> goes as a bearer header to the endpoint it belongs to, never into a URL or a message.
    /// </summary>
    public static async Task<int?> ReadContextAsync(HttpClient http, Uri endpoint, string model, CancellationToken ct, string? apiKey = null)
    {
        // vLLM serves /v1/models; an endpoint configured without /v1 is tried both ways.
        var modelUrls = ModelUrl.EndsWithVersion(endpoint)
            ? new[] { ModelUrl.Under(endpoint, "models") }
            : [ModelUrl.Under(endpoint, "models"), ModelUrl.Under(endpoint, "v1/models")];

        foreach (var url in modelUrls)
        {
            if (await TryAsync(http, url, root => FromModels(root, model), apiKey, ct) is { } vllm)
            {
                return vllm;
            }
        }

        return await TryAsync(http, ModelUrl.Root(endpoint, "props"), FromProps, apiKey, ct);
    }

    private static int? FromModels(JsonElement root, string model)
    {
        foreach (var entry in root.Prop("data").Items())
        {
            if (string.Equals(entry.Str("id"), model, StringComparison.Ordinal))
            {
                return Usable(entry.Prop("max_model_len"));
            }
        }

        return null;
    }

    private static int? FromProps(JsonElement root) => Usable(root.Prop("default_generation_settings").Prop("n_ctx"));

    private static int? Usable(JsonElement number) =>
        number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var value) && value > 0 ? value : null;

    private static async Task<int?> TryAsync(HttpClient http, Uri url, Func<JsonElement, int?> read, string? apiKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await ServiceHttp.SendAsync(http, request, Service, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var json = await JsonRead.ParseAsync(response, Service, MaxBodyBytes, ct);
            return read(json.RootElement);
        }
        catch (Exception e) when (!ct.IsCancellationRequested
            && e is ServiceException or HttpRequestException or JsonException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }
}
