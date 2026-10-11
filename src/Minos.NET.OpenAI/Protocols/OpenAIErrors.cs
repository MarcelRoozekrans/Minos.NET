using System.Globalization;
using System.Text.Json;

namespace Minos.OpenAI.Protocols;

/// <summary>
/// How <c>/v1/decisions</c> reports errors: the status decides the kind, as for System One, and a whole JSON body becomes
/// <see cref="DecisionError.Detail"/>. OpenAI's standard body, <c>{"error":{"message","type","param","code"}}</c>, which
/// the guide does not document for this endpoint, gives its <c>message</c>; any other body keeps the generic message.
/// </summary>
internal static class OpenAIErrors
{
    public static DecisionError Map(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter)
    {
        var kind = statusCode switch
        {
            401 or 403 => DecisionErrorKind.Unauthorized,
            400 or 422 => DecisionErrorKind.Validation,
            429 => DecisionErrorKind.RateLimited,
            503 or 529 => DecisionErrorKind.Overloaded,
            >= 500 => DecisionErrorKind.Server,
            _ => DecisionErrorKind.Http,
        };

        var detail = Detail(body, bodyTruncated, contentType);
        var message = detail is { } json && ErrorMessage(json) is { } text
            ? text
            : "The API returned HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ".";

        return new DecisionError(kind, message)
        {
            StatusCode = statusCode,
            RetryAfter = retryAfter,
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

    private static JsonElement? Detail(ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType)
    {
        if (body.IsEmpty || bodyTruncated || !IsJson(contentType))
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(body);
            using var document = JsonDocument.ParseValue(ref reader);

            // Reads past the root value, so trailing content after it is rejected as JsonDocument.Parse rejects it.
            reader.Read();
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJson(string? contentType)
        => contentType is not null
            && (string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
