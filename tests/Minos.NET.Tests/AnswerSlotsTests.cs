namespace Minos.Tests;

public sealed class AnswerSlotsTests
{
    private static readonly QuestionSetDefinition Definition = new(
        QuestionDefinition.Noul("n", "N?"),
        QuestionDefinition.Choice("c", "C?", new OptionDefinition("red", "R"), new OptionDefinition("green", "G"), new OptionDefinition("blue", "B")),
        QuestionDefinition.Score("s", "S?", "Low", "Mid", "High"));

    [Fact]
    public void Accessors_BuildTypedAnswers()
    {
        var probabilities = new double[] { 0.1, 0.7, 0.2, 0.0, 0.3, 0.7 };
        ReadOnlySpan<AnswerSlot> slots = [new(0, 0.9, 0, 0), new(1, 0, 0.8, 0), new(2, 1.7, 0.6, 3)];
        var answers = new AnswerSlots(slots, probabilities, Definition);

        Assert.Equal(3, answers.Count);
        Assert.Equal(0.9, answers.Noul(0).Probability);
        var choice = answers.Choice(1, ColorOptions.Instance);
        Assert.Equal(Color.Green, choice.Value);
        Assert.Equal(0.8, choice.Confidence);
        var score = answers.Score(2, UrgencyLevels.Instance);
        Assert.Equal(Urgency.High, score.Value);
        Assert.Equal(1.7, score.Expected);
    }

    [Fact]
    public void WrongKind_Throws()
        => Assert.Throws<InvalidOperationException>(() =>
            new AnswerSlots([new(0, 0.9, 0, 0), default, default], new double[6], Definition).Choice(0, ColorOptions.Instance));

    [Fact]
    public void OutOfRange_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnswerSlots([default, default, default], new double[6], Definition).Noul(3));

    [Fact]
    public void OptionCountMismatch_Throws()
        => Assert.Throws<ArgumentException>(() =>
            new AnswerSlots([default, new(0, 0, 0.5, 0), default], new double[6], Definition).Choice(1, ColorOptionsSubset.Instance));
}
