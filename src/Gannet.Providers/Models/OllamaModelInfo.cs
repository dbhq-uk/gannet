using System.Net.Http.Json;
using System.Text.Json;
using Gannet.Providers.Http;

namespace Gannet.Providers.Models;

internal static class OllamaModelInfo
{
    private const string Service = "Ollama";
    private const int UnknownContext = 4096;

    /// <summary>The trained context length and tool support, from POST /api/show.</summary>
    public static async Task<(int TrainedContext, bool SupportsTools)> ReadAsync(HttpClient http, Uri endpoint, string model, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ModelUrl.Under(endpoint, "api/show")) { Content = JsonContent.Create(new { model }) };
        using var response = await ServiceHttp.SendAsync(http, request, Service, ct);
        await HttpErrors.EnsureSuccessAsync(response, Service, website: false, ct);
        using var json = await JsonRead.ParseAsync(response, Service, ct);

        var info = json.RootElement.Prop("model_info");
        var architecture = info.Str("general.architecture");
        var length = architecture.Length > 0 ? info.Prop($"{architecture}.context_length") : default;
        var context = length.ValueKind == JsonValueKind.Number && length.TryGetInt32(out var trained) && trained > 0 ? trained : UnknownContext;
        var tools = json.RootElement.Prop("capabilities").Items().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "tools");
        return (context, tools);
    }
}
