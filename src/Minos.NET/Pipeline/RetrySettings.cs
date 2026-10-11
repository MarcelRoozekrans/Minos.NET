namespace Minos;

/// <summary>The rules for retry settings, shared by <see cref="DecisionRetryOptions"/> and every protocol package's client settings.</summary>
internal static class RetrySettings
{
    /// <summary>The longest span <see cref="HttpClient.Timeout"/> accepts, and the cap on the back-off options.</summary>
    internal static readonly TimeSpan MaxMilliseconds = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>Checks the retry count and the back-off span.</summary>
    /// <param name="maxRetries">The number of retries.</param>
    /// <param name="initialBackoff">The first back-off.</param>
    /// <param name="maxRetryDelay">The longest wait before a retry.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <exception cref="ArgumentException">A value is out of range.</exception>
    public static void Check(int maxRetries, TimeSpan initialBackoff, TimeSpan maxRetryDelay, string paramName)
    {
        if (maxRetries is < 0 or > 10)
        {
            throw new ArgumentException("MaxRetries must be between 0 and 10.", paramName);
        }

        if (initialBackoff <= TimeSpan.Zero || initialBackoff > MaxMilliseconds)
        {
            throw new ArgumentException("InitialBackoff must be positive and at most int.MaxValue milliseconds.", paramName);
        }

        if (maxRetryDelay < initialBackoff || maxRetryDelay > MaxMilliseconds)
        {
            throw new ArgumentException(
                "MaxRetryDelay must be at least InitialBackoff and at most int.MaxValue milliseconds.", paramName);
        }
    }

    /// <summary>Checks the failure predicate is set.</summary>
    /// <param name="shouldRetry">The predicate.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shouldRetry"/> is <see langword="null"/>.</exception>
    public static void CheckShouldRetry(object? shouldRetry, string paramName)
        => ArgumentNullException.ThrowIfNull(shouldRetry, paramName);
}
