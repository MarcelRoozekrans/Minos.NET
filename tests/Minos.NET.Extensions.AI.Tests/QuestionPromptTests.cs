using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI.Tests;

public sealed class QuestionPromptTests
{
    private static readonly QuestionDefinition Refund = QuestionDefinition.Noul(
        "refund", "Does the customer ask for a refund?", "They want their money back.", "They want something else.");

    private static readonly QuestionDefinition Team = QuestionDefinition.Choice(
        "team",
        "Which team handles it?",
        new OptionDefinition("billing", Criterion.Text("Payments and invoices.").WithExamples("I was charged twice.").WithNotFor("Shipping costs.")),
        new OptionDefinition("shipping", Criterion.Text("Deliveries and returns.")));

    private static readonly QuestionDefinition Urgency = QuestionDefinition.Score(
        "urgency", "How urgent is it?", "Can wait.", "Needs an answer today.", "Needs an answer now.");

    [Fact]
    public void HasOneSystemMessage_ThatAsksForOneLabel()
    {
        var messages = QuestionPrompt.Build(Team, Request(Team), verbalized: false);

        var system = Assert.Single(messages, message => message.Role == ChatRole.System);
        Assert.Contains("Answer with one label", system.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Choice_ListsTheLabelledOptions_ThenTheState()
    {
        var text = UserText(QuestionPrompt.Build(Team, Request(Team), verbalized: false));

        var question = text.IndexOf("Which team handles it?", StringComparison.Ordinal);
        var billing = text.IndexOf("A. billing: Payments and invoices.", StringComparison.Ordinal);
        var example = text.IndexOf("Example: I was charged twice.", StringComparison.Ordinal);
        var notFor = text.IndexOf("Not for: Shipping costs.", StringComparison.Ordinal);
        var shipping = text.IndexOf("B. shipping: Deliveries and returns.", StringComparison.Ordinal);
        var state = text.IndexOf("The parcel never came.", StringComparison.Ordinal);
        Assert.True(question >= 0, text);
        Assert.True(question < billing && billing < example && example < notFor && notFor < shipping && shipping < state, text);
    }

    [Fact]
    public void Noul_IsYesOrNo_WithItsMeanings()
    {
        var text = UserText(QuestionPrompt.Build(Refund, Request(Refund), verbalized: false));

        Assert.Contains("A. Yes: They want their money back.", text, StringComparison.Ordinal);
        Assert.Contains("B. No: They want something else.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Score_LabelsEachLevelByIndex()
    {
        var text = UserText(QuestionPrompt.Build(Urgency, Request(Urgency), verbalized: false));

        Assert.Contains("A. Level 0: Can wait.", text, StringComparison.Ordinal);
        Assert.Contains("B. Level 1: Needs an answer today.", text, StringComparison.Ordinal);
        Assert.Contains("C. Level 2: Needs an answer now.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbalized_AsksForTheProbabilityOfEachLabel()
    {
        var messages = QuestionPrompt.Build(Team, Request(Team), verbalized: true);

        var system = Assert.Single(messages, message => message.Role == ChatRole.System);
        Assert.Contains("probability of each label", system.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Answer with one label", system.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbalized_NamesTheJsonFormat_InTheUserMessage()
    {
        var text = UserText(QuestionPrompt.Build(Team, Request(Team), verbalized: true));

        Assert.EndsWith("Give the probability of each label, A or B, as a JSON object keyed by label.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_GoInAsDataParts()
    {
        var image = DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
        var request = Request(Refund) with { Images = [image] };

        var user = Assert.Single(QuestionPrompt.Build(Refund, request, verbalized: false), message => message.Role == ChatRole.User);

        var data = Assert.Single(user.Contents.OfType<DataContent>());
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(image.Data.ToArray(), data.Data.ToArray());
    }

    private static DecisionRequest Request(QuestionDefinition question)
        => new(new QuestionSetDefinition(question), DecisionContent.FromString("The parcel never came."));

    private static string UserText(IList<ChatMessage> messages)
        => Assert.Single(messages, message => message.Role == ChatRole.User).Text;
}
