using System.ClientModel;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using Anthropic.Exceptions;
using Gannet.Core.Services;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Exceptions;

namespace Gannet.Providers.Models;

/// <summary>
/// Turns what a model SDK throws into gannet's typed errors, for a whole call and for a failure part-way through a stream. The SDKs put the server's response text in their
/// messages (OpenAI's 401 carries a masked fragment of the key), so the original is never kept: no message is copied and no inner exception is attached. The caller's own
/// cancellation passes through. An exception gannet does not recognise passes through too, because it is a bug to see, not a service answer to describe.
/// </summary>
internal sealed class TypedErrorChatClient(IChatClient inner, string provider, string modelName) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception e) when (Map(e, cancellationToken) is { } mapped)
        {
            throw mapped;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate> updates;
        try
        {
            updates = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception e) when (Map(e, cancellationToken) is { } mapped)
        {
            throw mapped;
        }

        await using (updates)
        {
            while (true)
            {
                try
                {
                    if (!await updates.MoveNextAsync())
                    {
                        yield break;
                    }
                }
                catch (Exception e) when (Map(e, cancellationToken) is { } mapped)
                {
                    throw mapped;
                }

                yield return updates.Current;
            }
        }
    }

    private ServiceException? Map(Exception e, CancellationToken ct)
    {
        switch (e)
        {
            case OperationCanceledException when ct.IsCancellationRequested:
                return null;
            case OperationCanceledException:
                return new ServiceFailedException(provider, $"{provider} timed out.");
            case ClientResultException c:
                return c.Status > 0 ? FromStatus(c.Status, RetryAfter(c.GetRawResponse())) : Unreachable();
            case AnthropicApiException a:
                return FromStatus((int)a.StatusCode, null);
            case AnthropicIOException:
                return Unreachable();
            case AnthropicServiceException s:
                return FromAnthropicType(s.ErrorType);
            case AnthropicException:
                return new ServiceFailedException(provider, $"{provider} returned an error.");

            // Google's ApiException derives from HttpRequestException, so it must come first. Its message is the response body.
            case Google.GenAI.ApiException g:
                return g.StatusCode > 0 ? FromStatus(g.StatusCode, null) : Unreachable();
            case HttpRequestException h:
                return h.StatusCode is { } status ? FromStatus((int)status, null) : Unreachable();
            case OllamaException:
                return new ServiceFailedException(provider, $"{provider} returned an error.");
            default:
                return null;
        }
    }

    private ServiceException FromStatus(int status, TimeSpan? retryAfter) => status switch
    {
        401 or 403 => new AuthRequiredException(provider, $"{provider} refused the key for model '{modelName}' ({status}). Check Gannet:Models:{modelName}:ApiKey."),
        402 => new QuotaExhaustedException(provider, $"{provider} says the quota or credit is used up (402)."),
        429 => new RateLimitedException(provider, retryAfter, $"{provider} is limiting the request rate (429)."),
        _ => new ServiceFailedException(provider, $"{provider} answered {status}."),
    };

    // An error event inside an Anthropic stream has no HTTP status of its own; its type says what it was.
    private ServiceException FromAnthropicType(Anthropic.Models.ErrorType? type) => type switch
    {
        Anthropic.Models.ErrorType.AuthenticationError or Anthropic.Models.ErrorType.PermissionError => FromStatus(401, null),
        Anthropic.Models.ErrorType.BillingError => FromStatus(402, null),
        Anthropic.Models.ErrorType.RateLimitError => FromStatus(429, null),
        _ => new ServiceFailedException(provider, $"{provider} returned an error."),
    };

    private ServiceFailedException Unreachable() => new(provider, $"{provider} could not be reached.");

    private static TimeSpan? RetryAfter(System.ClientModel.Primitives.PipelineResponse? response)
    {
        if (response is null || !response.Headers.TryGetValue("Retry-After", out var value) || value is null)
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
