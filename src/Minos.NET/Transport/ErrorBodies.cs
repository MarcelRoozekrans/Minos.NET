using System.Globalization;
using System.Text.Json;

namespace Minos.Transport;

/// <summary>
/// The protocol-neutral half of mapping an error response: the status decides the kind, and a whole JSON body becomes
/// <see cref="DecisionError.Detail"/>. Each protocol decides its own message.
/// </summary>
internal static class ErrorBodies
{
    /// <summary>
    /// Maps an HTTP status to its error kind: 401 and 403 are unauthorized, 400 and 422 validation, 429 rate limited,
    /// 503 and 529 overloaded, any other 5xx server, and anything else HTTP.
    /// </summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <returns>The kind.</returns>
    public static DecisionErrorKind KindOf(int statusCode) => statusCode switch
    {
        401 or 403 => DecisionErrorKind.Unauthorized,
        400 or 422 => DecisionErrorKind.Validation,
        429 => DecisionErrorKind.RateLimited,
        503 or 529 => DecisionErrorKind.Overloaded,
        >= 500 => DecisionErrorKind.Server,
        _ => DecisionErrorKind.Http,
    };

    /// <summary>Gets the message for a status whose body gives none, such as "The API returned HTTP 400.".</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <returns>The message.</returns>
    public static string GenericMessage(int statusCode)
        => "The API returned HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>
    /// Parses a whole, untruncated body with a JSON media type into a detached element. Any other body, including one with
    /// content after its JSON value, gives <see langword="null"/>.
    /// </summary>
    /// <param name="body">The error body.</param>
    /// <param name="bodyTruncated">Whether the transport cut the body at its limit.</param>
    /// <param name="contentType">The body's media type, if any.</param>
    /// <returns>The body as JSON, or <see langword="null"/>.</returns>
    public static JsonElement? Detail(ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType)
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
