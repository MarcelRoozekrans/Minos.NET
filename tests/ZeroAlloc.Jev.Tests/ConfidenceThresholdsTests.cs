namespace ZeroAlloc.Jev.Tests;

public sealed class ConfidenceThresholdsTests
{
    [Fact]
    public void Default_IsTheDocumentedTiers()
    {
        Assert.Equal(0.5, ConfidenceThresholds.Default.Medium);
        Assert.Equal(0.9, ConfidenceThresholds.Default.High);
        Assert.Equal(0.5, default(ConfidenceThresholds).Medium);
        Assert.Equal(0.9, default(ConfidenceThresholds).High);
        Assert.Equal(0.5, new ConfidenceThresholds().Medium);
        Assert.Equal(0.9, new ConfidenceThresholds().High);
    }

    [Fact]
    public void Constructor_KeepsItsValues()
    {
        var thresholds = new ConfidenceThresholds(medium: 0.6, high: 0.85);

        Assert.Equal(0.6, thresholds.Medium);
        Assert.Equal(0.85, thresholds.High);
    }

    [Theory]
    [InlineData(0.0, ConfidenceTier.Low)]
    [InlineData(0.49, ConfidenceTier.Low)]
    [InlineData(0.5, ConfidenceTier.Medium)]
    [InlineData(0.89, ConfidenceTier.Medium)]
    [InlineData(0.9, ConfidenceTier.High)]
    [InlineData(1.0, ConfidenceTier.High)]
    [InlineData(double.NaN, ConfidenceTier.Low)]
    public void Default_Classify_PutsEachBoundaryInTheHigherTier(double confidence, ConfidenceTier expected)
        => Assert.Equal(expected, ConfidenceThresholds.Default.Classify(confidence));

    [Theory]
    [InlineData(0.59, ConfidenceTier.Low)]
    [InlineData(0.6, ConfidenceTier.Medium)]
    [InlineData(0.84, ConfidenceTier.Medium)]
    [InlineData(0.85, ConfidenceTier.High)]
    public void Custom_Classify_UsesItsOwnThresholds(double confidence, ConfidenceTier expected)
        => Assert.Equal(expected, new ConfidenceThresholds(0.6, 0.85).Classify(confidence));

    [Theory]
    [InlineData(0.69, ConfidenceTier.Low)]
    [InlineData(0.7, ConfidenceTier.High)]
    public void EqualThresholds_NeverGiveMedium(double confidence, ConfidenceTier expected)
        => Assert.Equal(expected, new ConfidenceThresholds(0.7, 0.7).Classify(confidence));

    [Theory]
    [InlineData(double.NaN, 0.9, "medium")]
    [InlineData(-0.01, 0.9, "medium")]
    [InlineData(1.01, 1.0, "medium")]
    [InlineData(0.5, double.NaN, "high")]
    [InlineData(0.5, -0.01, "high")]
    [InlineData(0.5, 1.01, "high")]
    [InlineData(0.8, 0.7, "medium")]
    public void Constructor_RejectsInvalidThresholds(double medium, double high, string parameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ConfidenceThresholds(medium, high));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void Constructor_AcceptsTheWholeRange()
    {
        Assert.Equal(ConfidenceTier.Medium, new ConfidenceThresholds(0.0, 1.0).Classify(0.0));
        Assert.Equal(ConfidenceTier.High, new ConfidenceThresholds(0.0, 1.0).Classify(1.0));
    }

    [Fact]
    public void Equality_ComparesTheEffectiveValues()
    {
        Assert.Equal(default, new ConfidenceThresholds(0.5, 0.9));
        Assert.True(default(ConfidenceThresholds) == new ConfidenceThresholds(0.5, 0.9));
        Assert.Equal(default(ConfidenceThresholds).GetHashCode(), new ConfidenceThresholds(0.5, 0.9).GetHashCode());
        Assert.True(new ConfidenceThresholds(0.6, 0.85) != ConfidenceThresholds.Default);
        Assert.False(ConfidenceThresholds.Default.Equals((object)0.5));
    }

    [Fact]
    public void Equality_DiffersWhenOnlyOneThresholdDiffers()
    {
        Assert.NotEqual(ConfidenceThresholds.Default, new ConfidenceThresholds(0.5, 0.8));
        Assert.NotEqual(ConfidenceThresholds.Default, new ConfidenceThresholds(0.6, 0.9));
        Assert.True(ConfidenceThresholds.Default != new ConfidenceThresholds(0.5, 0.8));
        Assert.True(ConfidenceThresholds.Default != new ConfidenceThresholds(0.6, 0.9));
    }

    [Theory]
    [InlineData(1.5, ConfidenceTier.High)]
    [InlineData(-0.1, ConfidenceTier.Low)]
    [InlineData(double.PositiveInfinity, ConfidenceTier.High)]
    [InlineData(double.NegativeInfinity, ConfidenceTier.Low)]
    public void Default_Classify_PlacesOutOfRangeInputsAtTheNearestEnd(double confidence, ConfidenceTier expected)
        => Assert.Equal(expected, ConfidenceThresholds.Default.Classify(confidence));

    [Fact]
    public void Constructor_AcceptsNegativeZero()
    {
        var thresholds = new ConfidenceThresholds(-0.0, 0.9);

        Assert.Equal(0.0, thresholds.Medium);
    }

    [Fact]
    public void Constructor_ChecksMediumBeforeHigh()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ConfidenceThresholds(double.NaN, double.NaN));

        Assert.Equal("medium", exception.ParamName);
    }
}
