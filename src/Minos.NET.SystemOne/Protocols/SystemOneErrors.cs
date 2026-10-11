using Minos.Transport;

namespace Minos.SystemOne.Protocols;

/// <summary>How <c>/v1/systemone</c> and OpenRouter report errors: the status decides the kind, and a whole JSON body becomes <see cref="DecisionError.Detail"/>.</summary>
internal static class SystemOneErrors
{
    public static DecisionError Map(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter, string? requestId)
        => new(ErrorBodies.KindOf(statusCode), ErrorBodies.GenericMessage(statusCode))
        {
            StatusCode = statusCode,
            RetryAfter = retryAfter,
            RequestId = requestId,
            Detail = ErrorBodies.Detail(body, bodyTruncated, contentType),
        };
}
