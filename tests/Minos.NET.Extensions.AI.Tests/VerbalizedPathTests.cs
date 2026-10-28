using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI.Tests;

public sealed class VerbalizedPathTests
{
    private const string TeamSchema =
        """{"type":"object","properties":{"A":{"type":"number","minimum":0,"maximum":1},"B":{"type":"number","minimum":0,"maximum":1},"C":{"type":"number","minimum":0,"maximum":1}},"required":["A","B","C"],"additionalProperties":false}""";

    private static readonly DecisionContent State = DecisionContent.FromString("I was charged twice.");

    private static readonly QuestionDefinition Refund = QuestionDefinition.Noul("refund", "Does the customer ask for a refund?");

    private static readonly QuestionDefinition Team = QuestionDefinition.Choice(
        "team", "Which team handles it?", new OptionDefinition("billing", null), new OptionDefinition("shipping", null), new OptionDefinition("sales", null));

    private static readonly QuestionDefinition Urgency = QuestionDefinition.Score("urgency", "How urgent is it?", "Low.", "Medium.", "High.");

    [Fact]
    public async Task ChatOptions_AskForTheJsonSchema_WithoutLogprobs()
    {
        var chat = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        using var client = Verbalized(chat, model: "options-model");

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var options = Assert.Single(chat.Calls).Options;
        Assert.NotNull(options);
        Assert.Equal(0f, options.Temperature);
        Assert.Equal("options-model", options.ModelId);
        Assert.Null(options.RawRepresentationFactory);
        var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.Equal("minos_answer", format.SchemaName);
        Assert.NotNull(format.Schema);
        Assert.Equal(TeamSchema, format.Schema.Value.GetRawText());
    }

    [Fact]
    public async Task Choice_TakesTheStatedProbabilities()
    {
        using var client = Verbalized(Answering("""{"A":0.7,"B":0.2,"C":0.1}"""));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var answer = SingleAnswer(result.Value);
        Assert.Equal(0, answer.ChosenIndex);
        Assert.Equal(0.7, answer.Confidence, 9);
        Assert.Equal([0.7, 0.2, 0.1], answer.Probabilities.ToArray(), (x, y) => Math.Abs(x - y) < 1e-9);
        Assert.Equal(ProbabilitySource.Verbalized, answer.ProbabilitySource);
        Assert.Equal(ProbabilitySource.Verbalized, result.Value.ProbabilitySource);
    }

    [Fact]
    public async Task Noul_ProbabilityIsTheNormalizedYes()
    {
        using var client = Verbalized(Answering("""{"A":0.6,"B":0.2}"""));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var answer = SingleAnswer(result.Value);
        Assert.Equal(0.75, answer.Probability, 9);
        Assert.Equal(ProbabilitySource.Verbalized, answer.ProbabilitySource);
    }

    [Fact]
    public async Task Score_ExpectedIsTheWeightedLevel()
    {
        using var client = Verbalized(Answering("""{"A":0.2,"B":0.3,"C":0.5}"""));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Urgency), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var answer = SingleAnswer(result.Value);
        Assert.Equal(2, answer.ChosenIndex);
        Assert.Equal(1.3, answer.Expected, 9);
        Assert.Equal(ProbabilitySource.Verbalized, answer.ProbabilitySource);
    }

    [Fact]
    public async Task AnExtraUnknownLabel_IsIgnored()
    {
        using var client = Verbalized(Answering("""{"A":0.7,"B":0.2,"C":0.1,"D":0.9}"""));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal([0.7, 0.2, 0.1], SingleAnswer(result.Value).Probabilities.ToArray(), (x, y) => Math.Abs(x - y) < 1e-9);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"A":0.7,"B":0.3}""")]
    [InlineData("""{"A":-0.1,"B":0.6,"C":0.5}""")]
    [InlineData("""{"A":0,"B":0,"C":0}""")]
    [InlineData("""{"A":"0.7","B":0.2,"C":0.1}""")]
    [InlineData("""[0.7,0.2,0.1]""")]
    [InlineData("")]
    public async Task AMalformedReply_IsInvalidResponse(string reply)
    {
        using var client = Verbalized(Answering(reply));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Contains("team", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedReply_KeepsTheStateAndTheReplyOutOfTheError()
    {
        using var client = Verbalized(Answering("""{"A":"SECRET-REPLY"}"""));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), DecisionContent.FromString("SECRET-STATE")));

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("SECRET", result.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModelWithoutLogprobs_AnswersInVerbalizedMode()
    {
        // The model the "no logprobs" error is about: that error advises Verbalized, so Verbalized must answer.
        var chat = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        using var logprobs = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });
        using var verbalized = Verbalized(chat);
        var request = new DecisionRequest(new QuestionSetDefinition(Team), State);

        var failed = await logprobs.EvaluateAsync(request);
        var answered = await verbalized.EvaluateAsync(request);

        Assert.True(failed.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, failed.Error.Kind);
        Assert.Contains("Verbalized", failed.Error.Message, StringComparison.Ordinal);
        Assert.True(answered.IsSuccess, answered.IsFailure ? answered.Error.Message : null);
        Assert.Equal(ProbabilitySource.Verbalized, SingleAnswer(answered.Value).ProbabilitySource);
    }

    [Fact]
    public async Task Auto_FallsBackToVerbalized_WhenTheResponseHasNoLogprobs()
    {
        var chat = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        using var client = chat.AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, chat.Calls.Count);
        Assert.NotNull(chat.Calls[0].Options!.RawRepresentationFactory);
        Assert.IsType<ChatResponseFormatJson>(chat.Calls[1].Options!.ResponseFormat);
        Assert.Equal(ProbabilitySource.Verbalized, SingleAnswer(result.Value).ProbabilitySource);
    }

    [Fact]
    public async Task Auto_FallsBackToVerbalized_WhenNoLabelIsAmongTheTopLogprobs()
    {
        var chat = new ScriptedChatClient((_, options) => options?.ResponseFormat is ChatResponseFormatJson
            ? ChatCompletions.WithoutLogprobs("""{"A":0.7,"B":0.2,"C":0.1}""")
            : ChatCompletions.WithTopLogprobs(("Billing", Ln(0.9)), ("Shipping", Ln(0.1))));
        using var client = chat.AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, chat.Calls.Count);
        Assert.Equal(ProbabilitySource.Verbalized, SingleAnswer(result.Value).ProbabilitySource);
    }

    [Fact]
    public async Task Auto_StaysOnLogprobs_WhenTheResponseHasThem()
    {
        var chat = new ScriptedChatClient((_, _) => ChatCompletions.WithTopLogprobs(("A", Ln(0.7)), ("B", Ln(0.2)), ("C", Ln(0.1))));
        using var client = chat.AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Single(chat.Calls);
        Assert.Equal(ProbabilitySource.Logprobs, SingleAnswer(result.Value).ProbabilitySource);
    }

    [Fact]
    public async Task Auto_KeepsNoMemoryBetweenCalls()
    {
        var chat = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        using var client = chat.AsDecisionClient();
        var request = new DecisionRequest(new QuestionSetDefinition(Team), State);

        var first = await client.EvaluateAsync(request);
        var second = await client.EvaluateAsync(request);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Message : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Message : null);
        Assert.Equal(4, chat.Calls.Count);
        Assert.NotNull(chat.Calls[2].Options!.RawRepresentationFactory);
        Assert.IsType<ChatResponseFormatJson>(chat.Calls[3].Options!.ResponseFormat);
    }

    [Fact]
    public async Task Auto_MakesNoThirdAttempt_WhenTheVerbalizedReplyIsMalformed()
    {
        var chat = Answering("not json");
        using var client = chat.AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(2, chat.Calls.Count);
    }

    private static QuestionAnswer SingleAnswer(DecisionResponse response)
    {
        Assert.Equal(1, response.Answers.Count);
        return response.Answers[0];
    }

    private static float Ln(double p) => (float)Math.Log(p);

    // A chat client that answers every call with this text and no logprobs, as a model without them does.
    private static ScriptedChatClient Answering(string reply) => new((_, _) => ChatCompletions.WithoutLogprobs(reply));

    private static ChatDecisionClient Verbalized(ScriptedChatClient chat, string? model = null)
        => chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Verbalized, Model = model });
}
