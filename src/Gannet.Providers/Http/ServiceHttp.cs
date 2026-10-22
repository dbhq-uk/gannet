using System.Text.Json;
using Gannet.Core.Services;

namespace Gannet.Providers.Http;

/// <summary>The request and status handling that search providers and remote readers share.</summary>
internal static class ServiceHttp
{
    private const int MaxErrorBodyBytes = 64 * 1024;

    /// <summary>
    /// Sends with the headers read first, so the body cap applies before anything is buffered, then maps the status. The quota check runs before the
    /// standard mapping: <paramref name="isQuota"/> sees the response and says whether it is a plan or credit limit. The caller disposes the response.
    /// </summary>
    public static async Task<HttpResponseMessage> SendCheckedAsync(
        HttpClient http,
        HttpRequestMessage request,
        string service,
        Func<HttpResponseMessage, CancellationToken, Task<bool>> isQuota,
        CancellationToken ct)
    {
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            if (await isQuota(response, ct))
            {
                throw new QuotaExhaustedException(service, $"{service} says the plan or credit limit is reached ({(int)response.StatusCode}).");
            }

            await HttpErrors.EnsureSuccessAsync(response, service, website: false, ct);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads a small JSON error body and returns what the predicate says about it. Anything wrong with the body (missing, too large, not JSON, bad UTF-8
    /// in a field the predicate reads) gives false, so the standard status mapping applies and nothing escapes.
    /// </summary>
    public static async Task<bool> ReadErrorBodyAsync(HttpResponseMessage response, string service, Func<JsonElement, bool> check, CancellationToken ct)
    {
        try
        {
            using var body = await JsonRead.ParseAsync(response, service, MaxErrorBodyBytes, ct);
            return check(body.RootElement);
        }
        catch (Exception e) when (e is ServiceFailedException or InvalidOperationException or JsonException or HttpRequestException or IOException)
        {
            return false;
        }
    }
}
