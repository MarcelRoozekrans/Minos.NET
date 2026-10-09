namespace Minos.Tests;

/// <summary>A clock frozen at one instant, so Retry-After dates are deterministic.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
