using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.Tests;

public enum OutOfOrderLevel
{
    High = 2,
    Low = 0,
    Medium = 1,
}

/// <summary>Runtime option sets over an enum: the same keys the generator emits, found without reflection.</summary>
public sealed class EnumOptionSetTests
{
    [Fact]
    public void Choice_KeysAreTheMemberNamesInSnakeCase()
    {
        var options = EnumOptionSet<Frustration>.ForChoice;

        Assert.Equal(3, options.Count);
        Assert.Equal(["calm", "frustrated", "very_angry"], new[] { options.KeyAt(0), options.KeyAt(1), options.KeyAt(2) });
        Assert.Equal("VeryAngry", options.NameAt(2));
        Assert.Equal(Frustration.VeryAngry, options[2]);
    }

    [Fact]
    public void Levels_FollowTheGivenOrder_KeyedByIndex()
    {
        // ForChoice holds OutOfOrderLevel by value: Low = 0, Medium = 1, High = 2. Levels in declaration order are
        // High, Low, Medium: the ForChoice indexes 2, 0, 1.
        var levels = EnumOptionSet<OutOfOrderLevel>.ForChoice.Levels([2, 0, 1]);

        Assert.Equal(3, levels.Count);
        Assert.Equal([OutOfOrderLevel.High, OutOfOrderLevel.Low, OutOfOrderLevel.Medium], new[] { levels[0], levels[1], levels[2] });
        Assert.Equal(["0", "1", "2"], new[] { levels.KeyAt(0), levels.KeyAt(1), levels.KeyAt(2) });
        Assert.Equal("High", levels.NameAt(0));
        Assert.Equal(1, levels.IndexOf(OutOfOrderLevel.Low));
    }

    [Fact]
    public void Alias_IsSkipped_AndTheFirstDeclaredNameIsKept()
    {
        // Priority declares Low = 10, High = 20, Legacy = 10. Enum.GetName is what the spec names; on .NET 10 it
        // returns the first declared name for an aliased value, which this test pins.
        var options = EnumOptionSet<Priority>.ForChoice;

        Assert.Equal(2, options.Count);
        Assert.Equal(["low", "high"], new[] { options.KeyAt(0), options.KeyAt(1) });
        Assert.Equal(0, options.IndexOf(Priority.Legacy));
    }

    [Fact]
    public void ChoiceOptions_AreInAscendingValueOrder()
    {
        // Enum.GetValues<T> orders by value, not declaration; Choice options follow it, as the spec decides.
        var options = EnumOptionSet<OutOfOrderLevel>.ForChoice;

        Assert.Equal([OutOfOrderLevel.Low, OutOfOrderLevel.Medium, OutOfOrderLevel.High], new[] { options[0], options[1], options[2] });
    }

    [Fact]
    public void IndexOf_AnUndefinedValue_IsMinusOne() => Assert.Equal(-1, EnumOptionSet<Department>.ForChoice.IndexOf((Department)99));

    [Fact]
    public void Indexer_OutOfRange_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EnumOptionSet<Department>.ForChoice[4]);

    [Fact]
    public void ChoiceSet_IsCreatedOncePerEnum() => Assert.Same(EnumOptionSet<Department>.ForChoice, EnumOptionSet<Department>.ForChoice);

    [Fact]
    public void ReadChoice_OverARuntimeSet_EqualsTheGeneratedSetsAnswer()
    {
        var generated = Answers.Parse<DepartmentRouting>(Fixture.Text("response-choice.json")).Department;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(
            """{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12,"sales":0.0},"confidence":0.81}"""));
        reader.Read();

        var runtime = JevAnswerReader.ReadChoice(ref reader, EnumOptionSet<Department>.ForChoice, new double[4], 0);

        Assert.Equal(generated, runtime);
    }
}
