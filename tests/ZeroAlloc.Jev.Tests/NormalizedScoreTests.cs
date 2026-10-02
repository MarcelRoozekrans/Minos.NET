namespace ZeroAlloc.Jev.Tests;

public sealed class NormalizedScoreTests
{
    [Theory]
    [InlineData(2, 0.0, 0.0)]
    [InlineData(2, 1.0, 1.0)]
    [InlineData(2, 0.4, 0.4)]
    [InlineData(10, 0.0, 0.0)]
    [InlineData(10, 9.0, 1.0)]
    [InlineData(10, 4.5, 0.5)]
    public void KeyedScore_DividesByTheTopLevelIndex(int levels, double expected, double normalized)
        => Assert.Equal(normalized, Keyed(levels, expected).Normalized, 12);

    [Theory]
    [InlineData(-0.000001, 0.0)]
    [InlineData(2.000001, 1.0)]
    public void Normalized_IsClampedToTheUnitRange(double expected, double normalized)
    {
        Assert.Equal(normalized, Keyed(3, expected).Normalized);
        Assert.Equal(normalized, Typed(expected).Normalized);
    }

    [Fact]
    public void Default_IsZero()
    {
        Assert.Equal(0.0, default(KeyedScore).Normalized);
        Assert.Equal(0.0, default(Score<Urgency>).Normalized);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.05)]
    [InlineData(2.0)]
    public void TypedAndKeyed_AgreeForTheSameAnswer(double expected)
        => Assert.Equal(Keyed(3, expected).Normalized, Typed(expected).Normalized);

    [Fact]
    public void Typed_UsesItsLevelCount()
        => Assert.Equal(0.525, Typed(1.05).Normalized, 12);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.7)]
    public void OneLevel_IsZeroWithoutDividingByZero(double expected)
        => Assert.Equal(0.0, Keyed(1, expected).Normalized);

    [Fact]
    public void NotANumberExpected_GivesNotANumber()
    {
        Assert.True(double.IsNaN(Keyed(3, double.NaN).Normalized));
        Assert.True(double.IsNaN(Typed(double.NaN).Normalized));
    }

    private static KeyedScore Keyed(int levels, double expected)
        => new(0, expected, 0.9, new KeyedProbabilityMap(new double[levels], 0, KeyedOptionSet.Levels(levels)));

    private static Score<Urgency> Typed(double expected)
        => new(Urgency.Medium, expected, 0.9, new ProbabilityMap<Urgency>([0.0, 1.0, 0.0], 0, UrgencyLevels.Instance));
}
