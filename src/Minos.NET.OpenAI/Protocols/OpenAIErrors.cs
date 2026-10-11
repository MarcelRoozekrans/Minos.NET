using System.Text.Json;
using Minos.Transport;

namespace Minos.OpenAI.Protocols;

/// <summary>
/// How <c>/v1/decisions</c> reports errors: the status decides the kind, as for System One, and a whole JSON body becomes
/// <see cref="DecisionError.Detail"/>. OpenAI's standard body, <c>{"error":{"message","type","param","code"}}</c>, which
/// the guide does not document for this endpoint, gives its <c>message</c>; any other body keeps the generic message.
/// </summary>
internal static class OpenAIErrors
{
    public static DecisionError Map(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter, string? requestId)
    {
        var detail = ErrorBodies.Detail(body, bodyTruncated, contentType);
        return new DecisionError(ErrorBodies.KindOf(statusCode), (detail is { } json ? ErrorMessage(json) : null) ?? ErrorBodies.GenericMessage(statusCode))
        {
            StatusCode = statusCode,
            RetryAfter = retryAfter,
            RequestId = requestId,
            Detail = detail,
        };
    }

    // error.message when it is a non-empty string.
    private static string? ErrorMessage(JsonElement body)
        => body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("error"u8, out var error)
            && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message"u8, out var message)
            && message.ValueKind == JsonValueKind.String
            && message.GetString() is { Length: > 0 } text
                ? text
                : null;
}
