using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace Gannet.Providers.Http;

/// <summary>
/// Which outcomes the HTTP clients retry. A safe method (GET, HEAD, OPTIONS) is retried on any transient outcome. Anything else, such as a paid search POST,
/// is retried only when the service answered 429 or 5xx, so a request that timed out or lost its connection is never silently sent twice.
/// </summary>
internal static class RetryRules
{
    public static ValueTask<bool> ShouldRetry(RetryPredicateArguments<HttpResponseMessage> args)
    {
        var method = args.Context.GetRequestMessage()?.Method;
        if (method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options)
        {
            return new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome));
        }

        return new ValueTask<bool>(args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.TooManyRequests or >= System.Net.HttpStatusCode.InternalServerError });
    }
}
