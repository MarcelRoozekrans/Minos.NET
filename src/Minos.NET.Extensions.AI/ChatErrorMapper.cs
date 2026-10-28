using System.ClientModel;
using System.ClientModel.Primitives;
using Minos.Transport;

namespace Minos.Extensions.AI;

/// <summary>Maps an exception from the chat client to a <see cref="DecisionError"/>, as the HTTP providers map their failures.</summary>
/// <remarks>
/// Each message is the bridge's own and never quotes the exception's: a provider's error text can echo the request, so it
/// stays in <see cref="DecisionError.Exception"/>.
/// </remarks>
internal static class ChatErrorMapper
{
    /// <summary>
    /// Maps an OpenAI SDK <see cref="ClientResultException"/> by its status: the kind as the HTTP providers map it, with
    /// <see cref="DecisionError.RetryAfter"/> and <see cref="DecisionError.RequestId"/> read from its headers. One without a
    /// response, an <see cref="HttpRequestException"/>, is <see cref="DecisionErrorKind.Network"/>; a
    /// <see cref="TimeoutException"/>, alone or inside a cancellation, is <see cref="DecisionErrorKind.Timeout"/>. Returns
    /// <see langword="null"/> for any other exception, which must propagate: Minos has no catch-all kind, and a bug must
    /// not pass as a provider failure.
    /// </summary>
    internal static DecisionError? Map(Exception exception) => exception switch
    {
        ClientResultException clientResult => clientResult.GetRawResponse() is { } response
            ? FromResponse(response, clientResult)
            : FromTransport(clientResult.InnerException, clientResult),
        HttpRequestException => Network(exception),
        TimeoutException or OperationCanceledException { InnerException: TimeoutException } => Timeout(exception),
        _ => null,
    };

    // The SDK raises a ClientResultException without a response when the request could not be sent or timed out.
    private static DecisionError FromTransport(Exception? inner, ClientResultException exception)
        => inner is TimeoutException or OperationCanceledException { InnerException: TimeoutException } ? Timeout(exception) : Network(exception);

    private static DecisionError FromResponse(PipelineResponse response, ClientResultException exception)
    {
        var status = response.Status;
        return new DecisionError(ErrorBodies.KindOf(status), ErrorBodies.GenericMessage(status))
        {
            StatusCode = status,
            RetryAfter = RetryAfter(response.Headers),
            RequestId = RequestId(response.Headers),
            Exception = exception,
        };
    }

    private static DecisionError Network(Exception exception)
        => new(DecisionErrorKind.Network, "The request could not be sent.") { Exception = exception };

    private static DecisionError Timeout(Exception exception)
        => new(DecisionErrorKind.Timeout, "The request timed out.") { Exception = exception };

    // retry-after-ms is more precise, so it wins when it is valid.
    private static TimeSpan? RetryAfter(PipelineResponseHeaders headers)
    {
        if (headers.TryGetValue("retry-after-ms", out var milliseconds)
            && milliseconds is not null
            && RetryAfterHeader.ParseMilliseconds(milliseconds) is { } delay)
        {
            return delay;
        }

        return headers.TryGetValue("Retry-After", out var retryAfter) && retryAfter is not null
            ? RetryAfterHeader.Parse(retryAfter, TimeProvider.System.GetUtcNow())
            : null;
    }

    // The same headers, in the same order, as the HTTP providers read.
    private static string? RequestId(PipelineResponseHeaders headers)
    {
        foreach (var name in RequestIdHeader.Names)
        {
            if (headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
