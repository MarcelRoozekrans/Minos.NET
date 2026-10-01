using ZeroAlloc.Jev.Telemetry;

namespace ZeroAlloc.Jev.Tests;

/// <summary>The telemetry names and values that are computed rather than written in an attribute.</summary>
public sealed class JevTelemetryTests
{
    [Fact]
    public void EveryErrorKind_HasItsNameAsErrorType()
    {
        foreach (var kind in Enum.GetValues<JevErrorKind>())
        {
            Assert.Equal(kind.ToString(), JevTelemetry.ErrorTypeOf(kind));
            Assert.Equal(kind.ToString(), new JevError(kind, "message").ErrorType);
        }
    }

    [Fact]
    public void UndefinedErrorKind_IsOther() => Assert.Equal("_OTHER", JevTelemetry.ErrorTypeOf((JevErrorKind)999));

    [Theory]
    [InlineData(JevProvider.TypeSafe, "typesafe")]
    [InlineData(JevProvider.OpenRouter, "openrouter")]
    public void Provider_HasItsGenAiValue(JevProvider provider, string expected) => Assert.Equal(expected, JevTelemetry.ProviderOf(provider));

    [Fact]
    public void Buckets_AreTheSpecsBoundaries()
    {
        Assert.Equal([0.01, 0.02, 0.04, 0.08, 0.16, 0.32, 0.64, 1.28, 2.56, 5.12, 10.24, 20.48, 40.96, 81.92], JevTelemetry.DurationBuckets.ToArray());
        Assert.Equal([1d, 4, 16, 64, 256, 1024, 4096, 16384, 65536, 262144, 1048576, 4194304, 16777216, 67108864], JevTelemetry.TokenBuckets.ToArray());
        Assert.Equal([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99], JevTelemetry.ConfidenceBuckets.ToArray());
    }
}
