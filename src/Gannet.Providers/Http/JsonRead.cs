using System.Globalization;
using System.Text.Json;
using Gannet.Core.Services;

namespace Gannet.Providers.Http;

internal static class JsonRead
{
    public static string Str(this JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    public static JsonElement Prop(this JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value : default;

    public static IEnumerable<JsonElement> Items(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];

    public static Uri? WebUri(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.UserInfo.Length == 0 ? uri : null;

    public static DateTimeOffset? Date(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>The most response body any search or reader call will read.</summary>
    public const int MaxBodyBytes = 16 * 1024 * 1024;

    public static Task<JsonDocument> ParseAsync(HttpResponseMessage response, string service, CancellationToken ct) =>
        ParseAsync(response, service, MaxBodyBytes, ct);

    public static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, string service, int maxBytes, CancellationToken ct)
    {
        var tooLarge = new ServiceFailedException(service, $"{service} returned a response that is too large.");
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw tooLarge;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw tooLarge;
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        }
        catch (JsonException)
        {
            throw new ServiceFailedException(service, $"{service} returned something that is not JSON.");
        }
    }
}
