using System.Net;
using Gannet.Core.Services;

namespace Gannet.Providers.Http;

internal static class HttpErrors
{
    /// <summary>
    /// Maps a response to a typed error, returned as a faulted task rather than thrown. Messages are gannet's own words: a body can echo a key or carry
    /// result text, so it is never copied into an exception. For a website (not an API), 401 and 403 mean
    /// the site blocked gannet, which is a challenge, not a key problem.
    /// </summary>
    public static Task EnsureSuccessAsync(HttpResponseMessage response, string service, bool website, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        if (response.Headers.TryGetValues("cf-mitigated", out var mitigated) && mitigated.Contains("challenge", StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromException(new ChallengeDetectedException(service, $"{service} hit a Cloudflare bot challenge."));
        }

        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        var status = (int)response.StatusCode;
        return Task.FromException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden when website =>
                new ChallengeDetectedException(service, $"The site refused the request ({status})."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new AuthRequiredException(service, $"{service} refused the request ({status}). Check its API key."),
            HttpStatusCode.PaymentRequired =>
                new QuotaExhaustedException(service, $"{service} says the quota or credit is used up (402)."),
            HttpStatusCode.TooManyRequests =>
                new RateLimitedException(service, RetryAfter(response), $"{service} is limiting the request rate (429)."),
            _ => new ServiceFailedException(service, $"{service} answered {status} ({response.StatusCode})."),
        });
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
