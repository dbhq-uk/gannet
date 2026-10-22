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

    /// <summary>How long a body read may take once the headers are in. A server that sends headers and then stalls must not hold the read until the caller gives up.</summary>
    public static readonly TimeSpan DefaultBodyDeadline = TimeSpan.FromSeconds(30);

    public static Task<JsonDocument> ParseAsync(HttpResponseMessage response, string service, CancellationToken ct, TimeSpan? deadline = null) =>
        ParseAsync(response, service, MaxBodyBytes, ct, deadline);

    public static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, string service, int maxBytes, CancellationToken ct, TimeSpan? deadline = null)
    {
        using var buffer = await ReadCappedAsync(response, service, maxBytes, ct, deadline ?? DefaultBodyDeadline);
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
    public static async Task<string> ReadTextAsync(HttpResponseMessage response, string service, int maxBytes, CancellationToken ct, TimeSpan? deadline = null)
    {
        using var buffer = await ReadCappedAsync(response, service, maxBytes, ct, deadline ?? DefaultBodyDeadline);
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// The Content-Length pre-check, then a counted read, so a body with no declared length cannot get past the cap. The read runs under a deadline linked to
    /// the caller's token; only the deadline's expiry (not the caller's cancellation) becomes a typed timeout.
    /// </summary>
    private static async Task<MemoryStream> ReadCappedAsync(HttpResponseMessage response, string service, int maxBytes, CancellationToken ct, TimeSpan deadline)
    {
        var tooLarge = new ServiceFailedException(service, $"{service} returned a response that is too large.");
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw tooLarge;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(deadline);
        var buffer = new MemoryStream();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, limit.Token)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw tooLarge;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer;
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await buffer.DisposeAsync();
            throw new ServiceFailedException(service, $"{service} timed out");
        }
        catch
        {
            await buffer.DisposeAsync();
            throw;
        }
    }
}
