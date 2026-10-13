namespace Minos;

/// <summary>How <see cref="RetryingDecisionClient"/> retries.</summary>
/// <remarks>
/// The settings are checked when a <see cref="RetryingDecisionClient"/> is created from them, which throws
/// <see cref="ArgumentException"/> for one out of range; the stage copies them, so changing them afterwards has no effect.
/// </remarks>
public sealed class DecisionRetryOptions
{
    /// <summary>Gets or sets the number of retries after the first attempt, from 0 to 10. 0 turns retrying off. Defaults to 2.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Gets or sets the first retry's backoff; each later retry doubles it, up to <see cref="MaxRetryDelay"/>. Must be positive and at most <see cref="int.MaxValue"/> milliseconds. Defaults to 500 ms.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets the longest wait before a retry, including one a response asked for. Must be at least <see cref="InitialBackoff"/> and at most <see cref="int.MaxValue"/> milliseconds. Defaults to 30 seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets whether each backoff is randomized, so many clients do not retry in step. Defaults to <see langword="true"/>.</summary>
    public bool Jitter { get; set; } = true;

    /// <summary>Gets or sets which failures are retried. A <see cref="DecisionErrorKind.Disposed"/> failure never is. Must not be <see langword="null"/>. Defaults to <see cref="IsTransient"/>.</summary>
    public Func<DecisionError, bool> ShouldRetry { get; set; } = IsTransient;

    /// <summary>Whether a failure is worth another attempt: rate limiting, overload, server errors, 408, network failures and time-outs.</summary>
    /// <param name="error">The failure.</param>
    /// <returns><see langword="true"/> when another attempt may succeed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static bool IsTransient(DecisionError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind is DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded or DecisionErrorKind.Server
                or DecisionErrorKind.Network or DecisionErrorKind.Timeout
            || error.StatusCode == 408;
    }

    internal DecisionRetryOptions Clone() => new()
    {
        MaxRetries = MaxRetries,
        InitialBackoff = InitialBackoff,
        MaxRetryDelay = MaxRetryDelay,
        Jitter = Jitter,
        ShouldRetry = ShouldRetry,
    };

    // The same rules and messages as SystemOneDecisionClientOptions' retry settings: see RetrySettings.
    internal void Validate(string paramName)
    {
        RetrySettings.CheckShouldRetry(ShouldRetry, paramName);
        RetrySettings.Check(MaxRetries, InitialBackoff, MaxRetryDelay, paramName);
    }
}
