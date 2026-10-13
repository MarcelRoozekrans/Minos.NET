using System.Threading.RateLimiting;

namespace Minos;

/// <summary>Adds <see cref="RateLimitingDecisionClient"/> to a pipeline.</summary>
public static class RateLimitingDecisionClientBuilderExtensions
{
    /// <summary>Adds a stage that takes one permit from <paramref name="limiter"/> for each call, before it is sent.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="limiter">The limiter. The caller owns it and may share it across clients; the stage never disposes it.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// Add it inside a cache stage, so a cache hit takes no permit, and inside a retry stage, so every attempt takes a
    /// permit and a rejection is retried after its <see cref="DecisionError.RetryAfter"/>.
    /// A client's built-in retries run inside every stage you add, so they take no extra permit.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="limiter"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseRateLimiting(this DecisionClientBuilder builder, RateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(limiter);
        return builder.Use(inner => new RateLimitingDecisionClient(inner, limiter));
    }
}
