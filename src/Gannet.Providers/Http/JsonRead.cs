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
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    public static DateTimeOffset? Date(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    public static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, string service, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new ServiceFailedException(service, $"{service} returned something that is not JSON.");
        }
    }
}
