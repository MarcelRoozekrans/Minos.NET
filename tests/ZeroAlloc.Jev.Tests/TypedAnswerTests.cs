namespace ZeroAlloc.Jev.Tests;

public sealed class TypedAnswerTests
{
    [Theory]
    [InlineData(0.95, true)]
    [InlineData(0.5, true)]
    [InlineData(0.49, false)]
    public void Noul_Value_IsProbabilityAtLeastHalf(double probability, bool expected)
    {
        var noul = new Noul(probability);

        Assert.Equal(probability, noul.Probability);
        Assert.Equal(expected, noul.Value);
    }

    [Fact]
    public void Choice_KeepsValues()
    {
        var map = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);

        var choice = new Choice<Color>(Color.Green, 0.81, map);

        Assert.Equal(Color.Green, choice.Value);
        Assert.Equal(0.81, choice.Confidence);
        Assert.Equal(0.7, choice.Probabilities[Color.Green]);
    }

    [Fact]
    public void Score_KeepsValues()
    {
        var map = new ProbabilityMap<Urgency>([0.0, 0.95, 0.05], 0, UrgencyLevels.Instance);

        var score = new Score<Urgency>(Urgency.Medium, 1.05, 0.92, map);

        Assert.Equal(Urgency.Medium, score.Value);
        Assert.Equal(1.05, score.Expected);
        Assert.Equal(0.92, score.Confidence);
        Assert.Equal(0.05, score.Probabilities[Urgency.High]);
    }

    [Fact]
    public void ProbabilityMap_IndexesByEnumValue_FromItsOffset()
    {
        var map = new ProbabilityMap<Color>([9.0, 0.2, 0.3, 0.5], 1, ColorOptions.Instance);

        Assert.Equal(3, map.Count);
        Assert.Equal(0.2, map[Color.Red]);
        Assert.Equal(0.3, map[Color.Green]);
        Assert.Equal(0.5, map[Color.Blue]);
    }

    [Fact]
    public void ProbabilityMap_ValueThatIsNotAnOption_Throws()
    {
        var map = new ProbabilityMap<Color>([0.2, 0.3, 0.5], 0, ColorOptions.Instance);

        Assert.Throws<ArgumentOutOfRangeException>(() => map[(Color)42]);
    }

    [Fact]
    public void ProbabilityMap_Enumerates_InDeclarationOrder()
    {
        var map = new ProbabilityMap<Color>([0.2, 0.3, 0.5], 0, ColorOptions.Instance);
        var items = new List<(Color Option, double Probability)>();

        foreach (var item in map)
        {
            items.Add(item);
        }

        Assert.Equal(new[] { (Color.Red, 0.2), (Color.Green, 0.3), (Color.Blue, 0.5) }, items);
    }

    [Fact]
    public void ProbabilityMap_Default_IsEmpty()
    {
        var map = default(ProbabilityMap<Color>);

        Assert.Equal(0, map.Count);
        Assert.False(map.GetEnumerator().MoveNext());
        Assert.Throws<ArgumentOutOfRangeException>(() => map[Color.Red]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ProbabilityMap_SliceOutsideBuffer_Throws(int offset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProbabilityMap<Color>([0.1, 0.2, 0.3], offset, ColorOptions.Instance));
    }
}
