using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Protocols;

namespace Minos.Tests;

public enum OutOfOrderLevel
{
    High = 2,
    Low = 0,
    Medium = 1,
}

public enum AliasAfterLarge
{
    Item00 = 0,
    Item01 = 1,
    Item02 = 2,
    Item03 = 3,
    Item04 = 4,
    Item05 = 5,
    Item06 = 6,
    Item07 = 7,
    Item08 = 8,
    Item09 = 9,
    Item10 = 10,
    Item11 = 11,
    Item12 = 12,
    Item13 = 13,
    Item14 = 14,
    Item15 = 15,
    Item16 = 16,
    Item17 = 17,
    Item18 = 18,
    Item19 = 19,
    Item20 = 20,
    Item21 = 21,
    Item22 = 22,
    Item23 = 23,

    // Deliberate alias: test data for the alias rule. On this enum Enum.GetName
    // returns "Legacy" for the value, which is why the option set reads the fields instead.
#pragma warning disable CA1069
    Legacy = Item03,
#pragma warning restore CA1069
}

public enum AliasBeforeLarge
{
    Item00 = 0,
    Item01 = 1,
    Item02 = 2,
    Item03 = 3,
    Item04 = 4,

    // Deliberate alias: test data for the alias rule; the generator keys the value as Old.
#pragma warning disable CA1069
    Old = 5,
    Current = 5,
#pragma warning restore CA1069
    Item06 = 6,
    Item07 = 7,
    Item08 = 8,
    Item09 = 9,
    Item10 = 10,
    Item11 = 11,
    Item12 = 12,
    Item13 = 13,
    Item14 = 14,
    Item15 = 15,
    Item16 = 16,
    Item17 = 17,
    Item18 = 18,
    Item19 = 19,
    Item20 = 20,
    Item21 = 21,
    Item22 = 22,
    Item23 = 23,
}

/// <summary>The generator's keys and order for the enums the runtime option sets are compared against.</summary>
[Questions]
public partial record DeclarationOrderChecks
{
    [Choice("Which one, after?")]
    public partial Choice<AliasAfterLarge> After { get; }

    [Choice("Which one, before?")]
    public partial Choice<AliasBeforeLarge> Before { get; }

    [Choice("Which level?")]
    public partial Choice<OutOfOrderLevel> OutOfOrder { get; }
}

/// <summary>Runtime option sets over an enum: the same keys, in the same order, as the generator emits.</summary>
public sealed class EnumOptionSetTests
{
    [Fact]
    public void Choice_KeysAreTheMemberNamesInSnakeCase()
    {
        var options = EnumOptionSet<Frustration>.ForChoice;

        Assert.Equal(3, options.Count);
        Assert.Equal(["calm", "frustrated", "very_angry"], Keys(options));
        Assert.Equal("VeryAngry", options.NameAt(2));
        Assert.Equal(Frustration.VeryAngry, options[2]);
    }

    [Fact]
    public void ChoiceOptions_AreInDeclarationOrder_AsTheGeneratorSendsThem()
    {
        // OutOfOrderLevel declares High = 2, Low = 0, Medium = 1: the options follow the declaration, not the values.
        var options = EnumOptionSet<OutOfOrderLevel>.ForChoice;

        Assert.Equal([OutOfOrderLevel.High, OutOfOrderLevel.Low, OutOfOrderLevel.Medium], new[] { options[0], options[1], options[2] });
        Assert.Equal(["high", "low", "medium"], Keys(options));
        Assert.Equal(GeneratedKeys(SystemOneProtocol.QuestionsUtf8(DeclarationOrderChecks.Definition), "out_of_order"), Keys(options));
    }

    [Fact]
    public void Levels_FollowTheGivenOrder_KeyedByIndex()
    {
        // ForChoice holds OutOfOrderLevel in declaration order: High, Low, Medium. Levels given lowest first as Low,
        // Medium, High are the ForChoice indexes 1, 2, 0.
        var levels = EnumOptionSet<OutOfOrderLevel>.ForChoice.Levels([1, 2, 0]);

        Assert.Equal(3, levels.Count);
        Assert.Equal([OutOfOrderLevel.Low, OutOfOrderLevel.Medium, OutOfOrderLevel.High], new[] { levels[0], levels[1], levels[2] });
        Assert.Equal(["0", "1", "2"], Keys(levels));
        Assert.Equal("Low", levels.NameAt(0));
        Assert.Equal(2, levels.IndexOf(OutOfOrderLevel.High));
    }

    [Fact]
    public void Alias_IsSkipped_AndTheFirstDeclaredNameIsKept()
    {
        // Priority declares Low = 10, High = 20, Legacy = 10: Legacy is an alias of Low, declared after it.
        var options = EnumOptionSet<Priority>.ForChoice;

        Assert.Equal(2, options.Count);
        Assert.Equal(["low", "high"], Keys(options));
        Assert.Equal("Low", options.NameAt(0));
        Assert.Equal(0, options.IndexOf(Priority.Legacy));

        // The generator sends the same two options in the same order. Its second key is "urgent" only because it reads
        // High's [Criteria(Key = "urgent")], which the builder does not.
        Assert.Equal(["low", "urgent"], GeneratedKeys(SystemOneProtocol.QuestionsUtf8(EdgeCases.Definition), "priority"));
    }

    [Fact]
    public void LargeEnum_AliasDeclaredAfter_KeepsTheFirstDeclaredName()
    {
        var options = EnumOptionSet<AliasAfterLarge>.ForChoice;

        Assert.Equal(24, options.Count);
        Assert.Equal("Item03", options.NameAt(3));
        Assert.Equal("item03", options.KeyAt(3));
        Assert.Equal(3, options.IndexOf(AliasAfterLarge.Legacy));
        Assert.DoesNotContain("legacy", Keys(options));
        Assert.Equal(GeneratedKeys(SystemOneProtocol.QuestionsUtf8(DeclarationOrderChecks.Definition), "after"), Keys(options));
    }

    [Fact]
    public void LargeEnum_AliasDeclaredBefore_KeepsTheFirstDeclaredName()
    {
        var options = EnumOptionSet<AliasBeforeLarge>.ForChoice;

        Assert.Equal(24, options.Count);
        Assert.Equal("Old", options.NameAt(5));
        Assert.Equal("old", options.KeyAt(5));
        Assert.Equal(5, options.IndexOf(AliasBeforeLarge.Current));
        Assert.DoesNotContain("current", Keys(options));
        Assert.Equal(GeneratedKeys(SystemOneProtocol.QuestionsUtf8(DeclarationOrderChecks.Definition), "before"), Keys(options));
    }

    [Fact]
    public void IndexOf_AnUndefinedValue_IsMinusOne() => Assert.Equal(-1, EnumOptionSet<Department>.ForChoice.IndexOf((Department)99));

    [Fact]
    public void Indexer_OutOfRange_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EnumOptionSet<Department>.ForChoice[4]);

    [Fact]
    public void ChoiceSet_IsCreatedOncePerEnum() => Assert.Same(EnumOptionSet<Department>.ForChoice, EnumOptionSet<Department>.ForChoice);

    // Holds by construction: the runtime set and the generator both list Department's members in declaration order.
    [Fact]
    public void ReadChoice_OverARuntimeSet_EqualsTheGeneratedSetsAnswer()
    {
        var generated = ResponseAnswers.Parse<DepartmentRouting>(Fixture.Text("response-choice.json")).Department;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(
            """{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12,"sales":0.0},"confidence":0.81}"""));
        reader.Read();

        var options = EnumOptionSet<Department>.ForChoice;
        var buffer = new double[4];
        var (index, confidence) = SystemOneAnswers.ReadChoice(ref reader, Utf8Keys.Encode(Keys(options)), buffer, 0);
        var runtime = new Choice<Department>(options[index], confidence, new ProbabilityMap<Department>(buffer, 0, options));

        Assert.Equal(generated, runtime);
    }

    // Annotated like every generic parameter that reaches EnumOptionSet<T>.
    private static string[] Keys<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(EnumOptionSet<T> options)
        where T : struct, Enum
    {
        var keys = new string[options.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = options.KeyAt(i);
        }

        return keys;
    }

    // The option keys the generator emits for one Choice of a generated set, in its wire order.
    private static string[] GeneratedKeys(ReadOnlySpan<byte> questionsUtf8, string questionKey)
        => [.. JsonNode.Parse(questionsUtf8)![questionKey]!["criteria"]!.AsObject().Select(pair => pair.Key)];
}
