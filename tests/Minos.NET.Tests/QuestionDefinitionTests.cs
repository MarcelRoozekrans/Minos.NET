namespace Minos.Tests;

public sealed class QuestionDefinitionTests
{
    [Fact]
    public void Score_KeysLevelsByPosition()
    {
        var question = QuestionDefinition.Score("urgency", "How urgent?", "Can wait", "This week", "Today");

        Assert.Equal(QuestionKind.Score, question.Kind);
        Assert.Equal(["0", "1", "2"], question.Options.Select(o => o.Key));
        Assert.All(question.Options, o => Assert.NotNull(o.Criterion));
    }

    [Fact]
    public void Choice_KeepsKeysAndNullCriteria()
    {
        var question = QuestionDefinition.Choice(
            "route_to", "Which team?", new OptionDefinition("billing", "Charges"), new OptionDefinition("other", null));

        Assert.Equal(["billing", "other"], question.Options.Select(o => o.Key));
        Assert.Null(question.Options[1].Criterion);
    }

    [Fact]
    public void Noul_HasNoOptions()
    {
        var question = QuestionDefinition.Noul("is_urgent", "Urgent?", whenFalse: "Not urgent");

        Assert.Empty(question.Options);
        Assert.Null(question.WhenTrue);
        Assert.True(question.WhenFalse!.Value.TryGetString(out var text));
        Assert.Equal("Not urgent", text);
    }

    [Fact]
    public void Choice_WithoutOptions_Throws()
        => Assert.Throws<ArgumentException>(() => QuestionDefinition.Choice("c", "Which?"));

    [Fact]
    public void Score_WithoutLevels_Throws()
        => Assert.Throws<ArgumentException>(() => QuestionDefinition.Score("s", "How much?"));

    [Fact]
    public void Set_RejectsDuplicateKeys()
        => Assert.Throws<ArgumentException>(() => new QuestionSetDefinition(
            QuestionDefinition.Noul("a", "A?"), QuestionDefinition.Noul("a", "Again?")));

    [Fact]
    public void Set_PrecomputesOffsetsAndKeys()
    {
        var set = new QuestionSetDefinition(
            QuestionDefinition.Noul("n", "N?"),
            QuestionDefinition.Choice("c", "C?", new OptionDefinition("x", "X"), new OptionDefinition("y", "Y")),
            QuestionDefinition.Score("s", "S?", "Low", "Mid", "High"));

        Assert.Equal([0, 0, 2], set.Offsets);
        Assert.Equal(5, set.ProbabilityCount);
        Assert.Equal("c"u8.ToArray(), set.KeysUtf8[1]);
        Assert.Equal("y"u8.ToArray(), set.OptionKeysUtf8[1][1]);
        Assert.Equal("2"u8.ToArray(), set.OptionKeysUtf8[2][2]);
    }

    [Fact]
    public void Set_ProtocolDataIsCreatedOnce()
    {
        var set = new QuestionSetDefinition(QuestionDefinition.Noul("n", "N?"));
        var calls = 0;
        var first = set.GetOrAddProtocolData(0, _ => { calls++; return new byte[] { 1 }; });
        var second = set.GetOrAddProtocolData(0, _ => { calls++; return new byte[] { 2 }; });

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }
}
