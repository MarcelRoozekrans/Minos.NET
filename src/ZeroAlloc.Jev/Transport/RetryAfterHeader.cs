using System.Globalization;

namespace ZeroAlloc.Jev.Transport;

/// <summary>Parses the HTTP <c>Retry-After</c> and <c>retry-after-ms</c> headers.</summary>
internal static class RetryAfterHeader
{
    /// <summary>The longest delay a header can produce; larger values are clamped to it.</summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    // RFC 9110 section 10.2.3 lets Retry-After be delta-seconds or an HTTP-date, and section 5.6.7 requires
    // recipients to accept all three HTTP-date forms: IMF-fixdate, RFC 850 and asctime.
    private static readonly string[] DateFormats =
    [
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        "dddd, dd-MMM-yy HH:mm:ss 'GMT'",
        "ddd MMM d HH:mm:ss yyyy",
    ];

    /// <summary>Returns the delay a <c>Retry-After</c> value asks for, or <see langword="null"/> when it cannot be read.</summary>
    /// <param name="value">Delta-seconds or an HTTP date, in any letter case.</param>
    /// <param name="now">The current time, for the date forms. A date in the past means no wait.</param>
    public static TimeSpan? Parse(string value, DateTimeOffset now)
    {
        var text = value.Trim();

        if (text.Length > 0 && IsAllDigits(text))
        {
            return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                && seconds < (ulong)MaxDelay.TotalSeconds
                    ? TimeSpan.FromSeconds(seconds)
                    : MaxDelay;
        }

        // Month and day names parse case-insensitively; the quoted GMT literal does not, so compare in upper case.
        if (DateTimeOffset.TryParseExact(
                text.ToUpperInvariant(),
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowInnerWhite,
                out var date))
        {
            var delay = date - now;
            return delay <= TimeSpan.Zero ? TimeSpan.Zero : delay > MaxDelay ? MaxDelay : delay;
        }

        return null;
    }

    /// <summary>Returns the delay a <c>retry-after-ms</c> value asks for, or <see langword="null"/> when it is not a non-negative number.</summary>
    /// <param name="value">Milliseconds, possibly fractional.</param>
    public static TimeSpan? ParseMilliseconds(string value)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var milliseconds)
            || double.IsNaN(milliseconds))
        {
            return null;
        }

        return milliseconds >= MaxDelay.TotalMilliseconds ? MaxDelay : TimeSpan.FromMilliseconds(milliseconds);
    }

    private static bool IsAllDigits(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
