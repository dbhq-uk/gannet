using System.Globalization;
using System.Text;
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
        using var buffer = await ReadCappedAsync(response, service, maxBytes, ct);
        try
        {
            return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        }
        catch (JsonException)
        {
            throw new ServiceFailedException(service, $"{service} returned something that is not JSON.");
        }
    }

    /// <summary>Reads a text body (UTF-8) under a cap the caller chooses: a Content-Length pre-check, then a counted read.</summary>
    public static async Task<string> ReadTextAsync(HttpResponseMessage response, string service, int maxBytes, CancellationToken ct)
    {
        using var buffer = await ReadCappedAsync(response, service, maxBytes, ct);
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>The Content-Length pre-check, then a counted read, so a body with no declared length cannot get past the cap.</summary>
    private static async Task<MemoryStream> ReadCappedAsync(HttpResponseMessage response, string service, int maxBytes, CancellationToken ct)
    {
        var tooLarge = new ServiceFailedException(service, $"{service} returned a response that is too large.");
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw tooLarge;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new MemoryStream();
        try
        {
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

            return buffer;
        }
        catch
        {
            await buffer.DisposeAsync();
            throw;
        }
    }
}
