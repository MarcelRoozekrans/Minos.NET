using Minos.Telemetry;

namespace Minos.Tests;

/// <summary>The telemetry names and values that are computed rather than written in an attribute.</summary>
public sealed class DecisionTelemetryTests
{
    [Fact]
    public void EveryErrorKind_HasItsNameAsErrorType()
    {
        foreach (var kind in Enum.GetValues<DecisionErrorKind>())
        {
            Assert.Equal(kind.ToString(), DecisionTelemetry.ErrorTypeOf(kind));
            Assert.Equal(kind.ToString(), new DecisionError(kind, "message").ErrorType);
        }
    }

    [Fact]
    public void UndefinedErrorKind_IsOther() => Assert.Equal("_OTHER", DecisionTelemetry.ErrorTypeOf((DecisionErrorKind)999));

    [Fact]
    public void Disposed_HasItsNameAsErrorType() => Assert.Equal("Disposed", DecisionTelemetry.ErrorTypeOf(DecisionErrorKind.Disposed));

    [Fact]
    public void NoErrorKind_HasTheValueZero_SoDefaultIsNoKind()
    {
        Assert.DoesNotContain(0, Enum.GetValues<DecisionErrorKind>().Select(kind => (int)kind));
        Assert.False(Enum.IsDefined(default(DecisionErrorKind)));
    }

    [Fact]
    public void ErrorKinds_AreNumberedFromOneInOrder()
        => Assert.Equal(Enumerable.Range(1, 12), Enum.GetValues<DecisionErrorKind>().Select(kind => (int)kind));

    [Theory]
    [InlineData(DecisionProvider.TypeSafe, "typesafe")]
    [InlineData(DecisionProvider.OpenRouter, "openrouter")]
    public void Provider_HasItsGenAiValue(DecisionProvider provider, string expected) => Assert.Equal(expected, DecisionTelemetry.ProviderOf(provider));

    [Fact]
    public void Buckets_AreTheSpecsBoundaries()
    {
        Assert.Equal([0.01, 0.02, 0.04, 0.08, 0.16, 0.32, 0.64, 1.28, 2.56, 5.12, 10.24, 20.48, 40.96, 81.92], DecisionTelemetry.DurationBuckets.ToArray());
        Assert.Equal([1d, 4, 16, 64, 256, 1024, 4096, 16384, 65536, 262144, 1048576, 4194304, 16777216, 67108864], DecisionTelemetry.TokenBuckets.ToArray());
        Assert.Equal([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99], DecisionTelemetry.ConfidenceBuckets.ToArray());
    }
}
