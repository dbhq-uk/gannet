using System.Net.Http.Headers;
using System.Text.Json;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Models;

/// <summary>
/// What OpenRouter says about one model. GET {endpoint}/models answers data[] with id, context_length, top_provider.context_length and supported_parameters
/// (openrouter.ai/docs/api-reference/models/get-models). The context window is top_provider.context_length when present, else context_length; tools are on when
/// supported_parameters contains "tools".
/// </summary>
internal static class OpenRouterModelInfo
{
    private const string Service = "OpenRouter";

    /// <summary>
    /// The window and tools OpenRouter reports for <paramref name="model"/>, or null when the call fails for any reason or the model is not listed. Only the caller's own
    /// cancellation escapes. The key goes as a bearer header to the endpoint it belongs to, never into a URL or a message.
    /// </summary>
    public static async Task<(int? ContextWindow, bool SupportsTools)?> ReadAsync(HttpClient http, Uri endpoint, string model, string apiKey, CancellationToken ct, TimeSpan? bodyDeadline = null)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelUrl.Under(endpoint, "models"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await ServiceHttp.SendAsync(http, request, Service, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // The whole catalogue is one response, so the default 16 MB cap and 30 s body deadline apply; nothing in the docs asks for more.
            using var json = await JsonRead.ParseAsync(response, Service, ct, bodyDeadline);
            foreach (var entry in json.RootElement.Prop("data").Items())
            {
                if (string.Equals(entry.Str("id"), model, StringComparison.Ordinal))
                {
                    return (Window(entry), entry.Prop("supported_parameters").Items().Any(p => p.ValueKind == JsonValueKind.String && p.GetString() == "tools"));
                }
            }

            return null;
        }
        catch (Exception e) when (!ct.IsCancellationRequested
            && e is ServiceException or HttpRequestException or JsonException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    private static int? Window(JsonElement entry) => Usable(entry.Prop("top_provider").Prop("context_length")) ?? Usable(entry.Prop("context_length"));

    private static int? Usable(JsonElement number) =>
        number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var value) && value > 0 ? value : null;
}
