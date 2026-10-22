using System.ClientModel.Primitives;

namespace Gannet.Providers.Models;

/// <summary>
/// How far the model SDKs may retry on their own. Both the OpenAI-format and the Anthropic SDK obey a service's Retry-After for as long as it says (probed: an hour),
/// whatever limit the SDK has, so a service that asks for a long wait would hold a run for that long. Two retries at most, each wait at most five seconds.
/// </summary>
internal static class ModelRetry
{
    public const int MaxRetries = 2;

    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

    /// <summary>The OpenAI-format SDK's retry policy: the default back-off and Retry-After handling, bounded.</summary>
    public sealed class Policy() : ClientRetryPolicy(MaxRetries)
    {
        protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
        {
            var delay = base.GetNextDelay(message, tryCount);
            return delay > MaxWait ? MaxWait : delay;
        }
    }

    /// <summary>
    /// Sends through another HttpClient and caps the Retry-After headers on the answer. The Anthropic SDK has a retry count but no hook for the wait, and reads
    /// Retry-After-Ms and Retry-After.
    /// </summary>
    public sealed class CapRetryAfter(HttpClient inner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The outer client has already marked this message as sent, and a message cannot be sent twice, so the inner client gets a copy.
            var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy, Content = request.Content };
            foreach (var header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var option in request.Options)
            {
                copy.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            }

            try
            {
                var response = await inner.SendAsync(copy, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                Cap(response.Headers);
                return response;
            }
            finally
            {
                // The content belongs to the SDK, which may send it again on a retry; disposing the copy must not dispose it.
                copy.Content = null;
                copy.Dispose();
            }
        }

        private static void Cap(System.Net.Http.Headers.HttpResponseHeaders headers)
        {
            var wait = headers.RetryAfter?.Delta ?? (headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (wait > MaxWait)
            {
                headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(MaxWait);
            }

            if (headers.TryGetValues("Retry-After-Ms", out var values)
                && (!double.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms) || ms > MaxWait.TotalMilliseconds))
            {
                headers.Remove("Retry-After-Ms");
                headers.TryAddWithoutValidation("Retry-After-Ms", ((int)MaxWait.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
