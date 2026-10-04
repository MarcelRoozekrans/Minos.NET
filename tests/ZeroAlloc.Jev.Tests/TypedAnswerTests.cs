using System.ComponentModel;
using System.Reflection;

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
    public void Noul_Equals_SameProbability_AreEqual()
    {
        var first = new Noul(0.81);
        var second = new Noul(0.81);

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Noul_Equals_DifferentProbability_AreNotEqual()
    {
        // Both answers are yes, so equality compares the probability, not Value.
        var first = new Noul(0.81);
        var second = new Noul(0.9);

        Assert.False(first.Equals(second));
        Assert.True(first != second);
        Assert.False(first.Equals((object)0.81));
        Assert.False(first.Equals(null));
    }

    [Fact]
    public void Noul_Default_EqualsZeroProbability()
    {
        Assert.Equal(new Noul(0.0), default);
        Assert.Equal(new Noul(0.0).GetHashCode(), default(Noul).GetHashCode());
    }

    [Fact]
    public void Noul_Equals_AllocatesNothing()
    {
        var first = new Noul(0.81);
        var second = new Noul(0.81);
        var comparer = EqualityComparer<Noul>.Default;

        // Warm up the JIT before measuring.
        _ = first.Equals(second);
        _ = comparer.Equals(first, second);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = first.Equals(second);
            _ = comparer.Equals(first, second);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }

    [Fact]
    public void JevOptionSet_IsHiddenFromIntelliSense_LikeJevAnswerReader()
    {
        Assert.Equal(EditorBrowsableState.Never, Hidden(typeof(JevAnswerReader)));
        Assert.Equal(EditorBrowsableState.Never, Hidden(typeof(JevOptionSet<>)));

        static EditorBrowsableState? Hidden(Type type)
            => type.GetCustomAttribute<EditorBrowsableAttribute>()?.State;
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

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => map[(Color)42]);

        Assert.Equal("option", exception.ParamName);
        Assert.Contains("option", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbabilityMap_Equals_SameProbabilities_DifferentBuffers_AreEqual()
    {
        var first = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);
        var second = new ProbabilityMap<Color>([9.0, 0.1, 0.7, 0.2], 1, ColorOptions.Instance);

        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ProbabilityMap_Equals_DifferentProbabilities_AreNotEqual()
    {
        var first = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);
        var second = new ProbabilityMap<Color>([0.1, 0.6, 0.3], 0, ColorOptions.Instance);

        Assert.False(first.Equals(second));
        Assert.True(first != second);
    }

    [Fact]
    public void ProbabilityMap_Equals_DifferentOptionCount_AreNotEqual()
    {
        var full = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);
        var subset = new ProbabilityMap<Color>([0.1, 0.9], 0, ColorOptionsSubset.Instance);

        Assert.False(full.Equals(subset));
    }

    [Fact]
    public void ProbabilityMap_Equals_SameCount_DifferentOptionOrder_AreNotEqual()
    {
        var forward = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);
        var reversed = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptionsReversed.Instance);

        Assert.False(forward.Equals(reversed));
    }

    [Fact]
    public void ProbabilityMap_Default_EqualsDefault()
    {
        var first = default(ProbabilityMap<Color>);
        var second = default(ProbabilityMap<Color>);

        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Indexer_SuccessPath_AllocatesNothing()
    {
        var map = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);

        // Warm up the JIT before measuring.
        _ = map[Color.Green];

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = map[Color.Green];
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }

    [Fact]
    public void ProbabilityMap_Equals_AllocatesNothing()
    {
        var first = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);
        var second = new ProbabilityMap<Color>([9.0, 0.1, 0.7, 0.2], 1, ColorOptions.Instance);

        // Warm up the JIT before measuring.
        _ = first.Equals(second);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = first.Equals(second);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }

    [Fact]
    public void Choice_Equals_AllocatesNothing()
    {
        var map = new ProbabilityMap<Color>([0.1, 0.7, 0.2], 0, ColorOptions.Instance);
        var first = new Choice<Color>(Color.Green, 0.81, map);
        var second = new Choice<Color>(Color.Green, 0.81, map);

        // Warm up the JIT before measuring.
        _ = first.Equals(second);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = first.Equals(second);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }

    [Fact]
    public void Score_Equals_AllocatesNothing()
    {
        var map = new ProbabilityMap<Urgency>([0.0, 0.95, 0.05], 0, UrgencyLevels.Instance);
        var first = new Score<Urgency>(Urgency.Medium, 1.05, 0.92, map);
        var second = new Score<Urgency>(Urgency.Medium, 1.05, 0.92, map);

        // Warm up the JIT before measuring.
        _ = first.Equals(second);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = first.Equals(second);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
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
