using System.Globalization;
using System.Text.Json;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Jev.Transport;

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
            HttpErrorKind.Timeout => new JevError(JevErrorKind.Timeout, "The request timed out.") { Exception = error.Exception },
            HttpErrorKind.Transport => new JevError(
                JevErrorKind.Network, error.Message ?? "The request could not be sent.") { Exception = error.Exception },
            HttpErrorKind.Deserialization => new JevError(
                JevErrorKind.InvalidResponse,
                error.Message ?? "The response could not be read.")
            {
                StatusCode = (int)error.StatusCode,
                Exception = error.Exception,
            },
            // Defensive: a future ZeroAlloc.Rest release may add a kind this mapper does not know about yet.
            _ => new JevError(
                JevErrorKind.InvalidResponse,
                "Unrecognized error kind " + error.Kind.ToString() + ".")
            {
                StatusCode = (int)error.StatusCode,
                Exception = error.Exception,
            },
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
            "The API returned HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".")
        {
            StatusCode = status,
            RetryAfter = RetryAfter(error),
            Detail = Detail(error),
        };
    }

    private TimeSpan? RetryAfter(HttpError error)
    {
        string? retryAfter = null;
        foreach (var header in error.Headers)
        {
            if (header.Value.Count == 0)
            {
                continue;
            }

            // retry-after-ms (used by TypeSafe's official SDKs) is more precise, so it wins when it is valid.
            if (string.Equals(header.Key, "retry-after-ms", StringComparison.OrdinalIgnoreCase)
                && RetryAfterHeader.ParseMilliseconds(header.Value[0]) is { } milliseconds)
            {
                return milliseconds;
            }

            if (string.Equals(header.Key, "Retry-After", StringComparison.OrdinalIgnoreCase))
            {
                retryAfter = header.Value[0];
            }
        }

        return retryAfter is null ? null : RetryAfterHeader.Parse(retryAfter, time.GetUtcNow());
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
