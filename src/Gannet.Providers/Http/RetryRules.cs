using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace Gannet.Providers.Http;

/// <summary>
/// Which outcomes the HTTP clients retry. A safe method (GET, HEAD, OPTIONS) is retried on any transient outcome. Anything else, such as a paid search POST,
/// is retried only when the service answered 429 or 5xx, so a request that timed out or lost its connection is never silently sent twice.
/// A 429 is retried only when it carries a Retry-After of at most <see cref="MaxRetryAfter"/>. Polly obeys that header for the delay and ignores its own maximum,
/// so a long wait (or none at all) must not be retried: the caller maps the 429 at once and decides what to do.
/// </summary>
internal static class RetryRules
{
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(5);

    public static ValueTask<bool> ShouldRetry(RetryPredicateArguments<HttpResponseMessage> args)
    {
        // A 429 follows one rule for every method (Brave's search is a GET).
        if (args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.TooManyRequests } limited)
        {
            return new ValueTask<bool>(ShortRetryAfter(limited));
        }

        var method = args.Context.GetRequestMessage()?.Method;
        if (method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options)
        {
            return new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome));
        }

        return new ValueTask<bool>(args.Outcome.Result is { StatusCode: >= System.Net.HttpStatusCode.InternalServerError });
    }

    private static bool ShortRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? wait = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return wait is { } w && w <= MaxRetryAfter;
    }
}
