using Minos.OpenAI.Protocols;

namespace Minos.OpenAI.Tests;

public sealed class OpenAICheckTests
{
    private const string NoulMessage = "OpenAI does not support WhenTrue or WhenFalse on a Noul; put the meaning in the instructions.";
    private const string CriterionMessage = "OpenAI does not support Examples or NotFor on a criterion; describe them in its text.";

    [Fact]
    public void Noul_WithWhenTrue_IsOneMin204Failure()
    {
        var failures = Collect(new QuestionSetDefinition(QuestionDefinition.Noul("refund", "Is this a refund?", whenTrue: "Money back")));

        Assert.Equal([new QuestionFailure("MIN204", "refund", NoulMessage)], failures);
    }

    [Fact]
    public void Noul_WithWhenFalse_IsOneMin204Failure()
    {
        var failures = Collect(new QuestionSetDefinition(QuestionDefinition.Noul("refund", "Is this a refund?", whenFalse: "Anything else")));

        Assert.Equal([new QuestionFailure("MIN204", "refund", NoulMessage)], failures);
    }

    [Fact]
    public void ChoiceOptionWithExamples_AndScoreLevelWithNotFor_AreOneMin205FailureEach_PerQuestion()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice(
                "topic",
                "Which topic?",
                new OptionDefinition("billing", Criterion.Text("Payments").WithExamples("I was charged twice")),
                new OptionDefinition("tech", Criterion.Text("Bugs").WithExamples("It crashes")),
                new OptionDefinition("other", null)),
            QuestionDefinition.Score("urgency", "How urgent?", Criterion.Text("Low").WithNotFor("Data loss"), "High"));

        var failures = Collect(definition);

        Assert.Equal(
            [new QuestionFailure("MIN205", "topic", CriterionMessage), new QuestionFailure("MIN205", "urgency", CriterionMessage)],
            failures);
    }

    [Fact]
    public void ASetWithNeither_AddsNothing()
    {
        List<QuestionFailure>? failures = null;

        OpenAIDecisionsProtocol.Instance.CollectUnsupported(TestSets.AllKinds(), "OpenAI", ref failures);

        Assert.Null(failures);
    }

    [Fact]
    public void Failures_ComeInQuestionOrder_AfterTheOnesAlreadyCollected_AndNameTheProvider()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Score("urgency", "How urgent?", Criterion.Text("Low").WithNotFor("Data loss"), "High"),
            QuestionDefinition.Noul("refund", "Is this a refund?", whenTrue: "Money back"),
            QuestionDefinition.Choice("topic", "Which topic?", new OptionDefinition("billing", Criterion.Text("Payments").WithExamples("Charged twice"))));
        var earlier = new QuestionFailure("MIN201", "x", "earlier");
        List<QuestionFailure>? failures = [earlier];

        OpenAIDecisionsProtocol.Instance.CollectUnsupported(definition, "openai-test", ref failures);

        Assert.Equal(
            [
                earlier,
                new QuestionFailure("MIN205", "urgency", "openai-test does not support Examples or NotFor on a criterion; describe them in its text."),
                new QuestionFailure("MIN204", "refund", "openai-test does not support WhenTrue or WhenFalse on a Noul; put the meaning in the instructions."),
                new QuestionFailure("MIN205", "topic", "openai-test does not support Examples or NotFor on a criterion; describe them in its text."),
            ],
            failures!);
    }

    private static List<QuestionFailure> Collect(QuestionSetDefinition definition)
    {
        List<QuestionFailure>? failures = null;
        OpenAIDecisionsProtocol.Instance.CollectUnsupported(definition, "OpenAI", ref failures);
        return Assert.IsType<List<QuestionFailure>>(failures);
    }
}
