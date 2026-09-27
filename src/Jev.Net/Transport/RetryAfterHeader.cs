using System.Globalization;

namespace Jev.Net.Transport;

/// <summary>Parses the HTTP <c>Retry-After</c> header in both of its forms.</summary>
internal static class RetryAfterHeader
{
    /// <summary>Returns the delay the header asks for, or <see langword="null"/> when it cannot be read.</summary>
    /// <param name="value">The header value: delta-seconds, or an HTTP date.</param>
    /// <param name="now">The current time, for the HTTP-date form. A date in the past means no wait.</param>
    public static TimeSpan? Parse(string value, DateTimeOffset now)
    {
        var text = value.Trim();

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParseExact(text, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var delay = date - now;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
