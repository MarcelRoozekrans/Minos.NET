namespace Minos.Extensions.AI.Tests;

public sealed class LabelDistributionTests
{
    private static (string Token, double Logprob) T(string token, double p) => (token, Math.Log(p));

    private static void AssertDistribution(double[] expected, double[]? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 9);
        }
    }

    [Fact]
    public void Label_IsTheLetterForTheIndex()
    {
        Assert.Equal("A", LabelDistribution.Label(0));
        Assert.Equal("Z", LabelDistribution.Label(25));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(26)]
    public void Label_OutOfRange_Throws(int index) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelDistribution.Label(index));

    [Fact]
    public void Logprobs_SoftmaxOverLabels() =>
        AssertDistribution([0.6, 0.3, 0.1], LabelDistribution.FromLogprobs(3, [T("A", 0.6), T("B", 0.3), T("C", 0.1)]));

    [Fact]
    public void Logprobs_TrimmedTokensAreSummed() =>
        AssertDistribution([0.6, 0.4], LabelDistribution.FromLogprobs(2, [T("A", 0.4), T(" A", 0.2), T("B", 0.4)]));

    [Fact]
    public void Logprobs_LowercaseDoesNotCount() =>
        AssertDistribution([0.9, 0.1], LabelDistribution.FromLogprobs(2, [T("a", 0.9), T("B", 0.1)]));

    [Fact]
    public void Logprobs_MissingLabelsShareTheLeftover() =>
        AssertDistribution([0.5, 0.3, 0.1, 0.1], LabelDistribution.FromLogprobs(4, [T("A", 0.5), T("B", 0.3)]));

    [Fact]
    public void Logprobs_NoLabel_IsNull() =>
        Assert.Null(LabelDistribution.FromLogprobs(2, [T("yes", 0.7), T("no", 0.3)]));

    [Fact]
    public void Stated_IsNormalized() =>
        AssertDistribution([0.5, 0.25, 0.25], LabelDistribution.FromStated(3, [2, 1, 1]));

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(-0.5, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(0.0, 0.0)]
    public void Stated_Malformed_IsNull(double first, double second) =>
        Assert.Null(LabelDistribution.FromStated(2, [first, second]));

    [Fact]
    public void Stated_WrongCount_IsNull() =>
        Assert.Null(LabelDistribution.FromStated(3, [0.5, 0.5]));

    [Fact]
    public void Pick_TieTakesTheLowerIndex()
    {
        var (picked, confidence) = LabelDistribution.Pick([0.4, 0.4, 0.2]);

        Assert.Equal(0, picked);
        Assert.Equal(0.4, confidence, 9);
    }

    [Fact]
    public void Expected_IsTheWeightedLevel() =>
        Assert.Equal(1.1, LabelDistribution.Expected([0.2, 0.5, 0.3]), 9);
}
