using System.Globalization;
using System.Text.Json;
using ZeroAlloc.Rest;

namespace Jev.Net.Transport;

/// <summary>Maps every ZeroAlloc.Rest failure to a <see cref="JevError"/>.</summary>
/// <param name="time">The clock used to turn an HTTP-date <c>Retry-After</c> into a delay.</param>
internal sealed class JevErrorMapper(TimeProvider time) : IHttpErrorMapper<JevError>
{
    public JevError Map(HttpError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Kind switch
        {
            HttpErrorKind.Status => FromStatus(error),
            HttpErrorKind.Timeout => new JevError(JevErrorKind.Timeout, "The request timed out.", exception: error.Exception),
            HttpErrorKind.Transport => new JevError(
                JevErrorKind.Network, error.Message ?? "The request could not be sent.", exception: error.Exception),
            _ => new JevError(
                JevErrorKind.InvalidResponse,
                error.Message ?? "The response could not be read.",
                (int)error.StatusCode,
                exception: error.Exception),
        };
    }

    private JevError FromStatus(HttpError error)
    {
        var status = (int)error.StatusCode;
        var kind = status switch
        {
            401 or 403 => JevErrorKind.Unauthorized,
            400 or 422 => JevErrorKind.Validation,
            429 => JevErrorKind.RateLimited,
            503 or 529 => JevErrorKind.Overloaded,
            >= 500 => JevErrorKind.Server,
            _ => JevErrorKind.Http,
        };

        return new JevError(
            kind,
            "The API returned HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
            status,
            RetryAfter(error),
            Detail(error));
    }

    private TimeSpan? RetryAfter(HttpError error)
    {
        foreach (var header in error.Headers)
        {
            if (string.Equals(header.Key, "Retry-After", StringComparison.OrdinalIgnoreCase) && header.Value.Count > 0)
            {
                return RetryAfterHeader.Parse(header.Value[0], time.GetUtcNow());
            }
        }

        return null;
    }

    private static JsonElement? Detail(HttpError error)
    {
        if (error.Body.IsEmpty || error.BodyTruncated || !IsJson(error.ContentType))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(error.Body);
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
