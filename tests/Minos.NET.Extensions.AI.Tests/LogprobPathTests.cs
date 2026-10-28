using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace Minos.Extensions.AI.Tests;

public sealed class LogprobPathTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("I was charged twice.");

    private static readonly QuestionDefinition Refund = QuestionDefinition.Noul("refund", "Does the customer ask for a refund?");

    private static readonly QuestionDefinition Team = QuestionDefinition.Choice(
        "team", "Which team handles it?", new OptionDefinition("billing", null), new OptionDefinition("shipping", null), new OptionDefinition("sales", null));

    private static readonly QuestionDefinition Urgency = QuestionDefinition.Score("urgency", "How urgent is it?", "Low.", "Medium.", "High.");

    [Theory]
    [InlineData(null, "options-model")]
    [InlineData("request-model", "request-model")]
    public async Task ChatOptions_AskForOneTokenWithTopLogprobs(string? requestModel, string expectedModel)
    {
        var chat = Answering(ChatCompletions.WithTopLogprobs(("A", Ln(0.8)), ("B", Ln(0.2))));
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs, Model = "options-model" });

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State) { Model = requestModel });

        Assert.True(result.IsSuccess);
        var options = Assert.Single(chat.Calls).Options;
        Assert.NotNull(options);
        Assert.Equal(1, options.MaxOutputTokens);
        Assert.Equal(0f, options.Temperature);
        Assert.Equal(expectedModel, options.ModelId);
        Assert.NotNull(options.RawRepresentationFactory);
        var raw = Assert.IsType<ChatCompletionOptions>(options.RawRepresentationFactory(chat));
        Assert.True(raw.IncludeLogProbabilities);
        Assert.Equal(20, raw.TopLogProbabilityCount);
    }

    [Theory]
    [InlineData(ProbabilityMode.Logprobs)]
    [InlineData(ProbabilityMode.Auto)]
    public async Task Noul_ProbabilityIsTheSoftmaxOfYes(ProbabilityMode mode)
    {
        using var client = Answering(ChatCompletions.WithTopLogprobs(("A", Ln(0.8)), ("B", Ln(0.2))))
            .AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = mode });

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var answer = SingleAnswer(result.Value);
        Assert.Equal(QuestionKind.Noul, answer.Kind);
        Assert.Equal(0.8, answer.Probability, 6);
        Assert.Equal(ProbabilitySource.Logprobs, answer.ProbabilitySource);
    }

    [Fact]
    public async Task Choice_PicksTheMostLikelyOption()
    {
        using var client = Logprobs(ChatCompletions.WithTopLogprobs(("B", Ln(0.6)), ("A", Ln(0.3)), ("C", Ln(0.1))));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var answer = SingleAnswer(result.Value);
        var expected = LabelDistribution.FromLogprobs(3, [("B", Ln(0.6)), ("A", Ln(0.3)), ("C", Ln(0.1))])!;
        var (picked, confidence) = LabelDistribution.Pick(expected);
        Assert.Equal(1, picked);
        Assert.Equal(picked, answer.ChosenIndex);
        Assert.Equal(confidence, answer.Confidence, 9);
        Assert.Equal(expected, answer.Probabilities.ToArray());
        Assert.Equal(ProbabilitySource.Logprobs, answer.ProbabilitySource);
    }

    [Fact]
    public async Task Score_ExpectedIsTheWeightedLevel()
    {
        using var client = Logprobs(ChatCompletions.WithTopLogprobs(("C", Ln(0.5)), ("B", Ln(0.3)), ("A", Ln(0.2))));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Urgency), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var answer = SingleAnswer(result.Value);
        var expected = LabelDistribution.FromLogprobs(3, [("C", Ln(0.5)), ("B", Ln(0.3)), ("A", Ln(0.2))])!;
        var (picked, confidence) = LabelDistribution.Pick(expected);
        Assert.Equal(QuestionKind.Score, answer.Kind);
        Assert.Equal(2, answer.ChosenIndex);
        Assert.Equal(picked, answer.ChosenIndex);
        Assert.Equal(confidence, answer.Confidence, 9);
        Assert.Equal(LabelDistribution.Expected(expected), answer.Expected, 9);
        Assert.Equal(ProbabilitySource.Logprobs, answer.ProbabilitySource);
    }

    [Fact]
    public async Task Response_ReportsTheModelTheChatResponseNames()
    {
        var response = ChatCompletions.WithTopLogprobs(("A", Ln(0.8)), ("B", Ln(0.2)));
        response.ModelId = "served-model";
        using var client = Logprobs(response);

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsSuccess);
        Assert.Equal("served-model", result.Value.Model);
        Assert.Equal(ProbabilitySource.Logprobs, result.Value.ProbabilitySource);
    }

    [Fact]
    public async Task FirstTokenWithoutTopLogprobs_CountsAlone()
    {
        using var client = Logprobs(ChatCompletions.WithFirstTokenOnly("B", Ln(0.9)));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(0.1, SingleAnswer(result.Value).Probability, 6);
    }

    [Fact]
    public async Task EachQuestion_IsAskedInItsOwnCall()
    {
        var chat = new ScriptedChatClient((messages, _) => messages.Last().Text.Contains("Which team", StringComparison.Ordinal)
            ? ChatCompletions.WithTopLogprobs(("C", Ln(0.7)), ("A", Ln(0.3)))
            : ChatCompletions.WithTopLogprobs(("B", Ln(0.6)), ("A", Ln(0.4))));
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund, Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, chat.Calls.Count);
        Assert.Equal(0.4, result.Value.Answers[0].Probability, 6);
        Assert.Equal(2, result.Value.Answers[1].ChosenIndex);
    }

    [Fact]
    public async Task NoLogprobs_IsUnsupported_AndNamesTheModel()
    {
        using var client = Answering(ChatCompletions.WithoutLogprobs())
            .AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs, Model = "no-logprobs-model" });

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Contains("no-logprobs-model", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("returned no logprobs", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoLogprobs_WithoutRawCompletion_IsUnsupported()
    {
        using var client = Logprobs(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "A")));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
    }

    [Fact]
    public async Task NoLabelInTheTopTokens_IsInvalidResponse()
    {
        using var client = Logprobs(ChatCompletions.WithTopLogprobs(("Yes", Ln(0.9)), ("No", Ln(0.1))));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Contains("refund", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScoreWithMoreLevelsThanLabels_IsUnsupportedMIN203_AndSendsNothing()
    {
        var chat = new ScriptedChatClient((_, _) => throw new InvalidOperationException("The chat client was called."));
        using var client = chat.AsDecisionClient();
        var levels = Enumerable.Range(0, 27).Select(i => Criterion.Text("Level " + i)).ToArray();
        var definition = new QuestionSetDefinition(QuestionDefinition.Score("severity", "How severe is it?", levels));

        var result = await client.EvaluateAsync(new DecisionRequest(definition, State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        var failure = Assert.Single(result.Error.Failures);
        Assert.Equal("MIN203", failure.Rule);
        Assert.Equal("severity", failure.QuestionKey);
        Assert.Contains("severity", failure.Message, StringComparison.Ordinal);
        Assert.Empty(chat.Calls);
    }

    [Theory]
    [MemberData(nameof(FailingResponses))]
    public async Task TheStateStaysOutOfErrors(Func<ChatResponse> respond)
    {
        using var client = Logprobs(respond());

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), DecisionContent.FromString("SECRET-STATE")));

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("SECRET-STATE", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-STATE", result.Error.ToString(), StringComparison.Ordinal);
        Assert.All(result.Error.Failures, failure => Assert.DoesNotContain("SECRET-STATE", failure.Message, StringComparison.Ordinal));
    }

    public static TheoryData<Func<ChatResponse>> FailingResponses() => new()
    {
        () => ChatCompletions.WithoutLogprobs("SECRET-STATE"),
        () => ChatCompletions.WithTopLogprobs(("SECRET-STATE", Ln(0.9))),
    };

    private static QuestionAnswer SingleAnswer(DecisionResponse response)
    {
        Assert.Equal(1, response.Answers.Count);
        return response.Answers[0];
    }

    private static float Ln(double p) => (float)Math.Log(p);

    private static ScriptedChatClient Answering(ChatResponse response) => new((_, _) => response);

    private static ChatDecisionClient Logprobs(ChatResponse response)
        => Answering(response).AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });
}
