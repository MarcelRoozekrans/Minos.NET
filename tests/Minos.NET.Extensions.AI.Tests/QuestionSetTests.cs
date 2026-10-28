using Microsoft.Extensions.AI;
using Minos.Recording;

namespace Minos.Extensions.AI.Tests;

public sealed class QuestionSetTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static readonly DecisionContent State = DecisionContent.FromString("I was charged twice.");

    private static readonly QuestionDefinition Refund = QuestionDefinition.Noul("refund", "Does the customer ask for a refund?");

    private static readonly QuestionDefinition Team = QuestionDefinition.Choice(
        "team", "Which team handles it?", new OptionDefinition("billing", null), new OptionDefinition("shipping", null), new OptionDefinition("sales", null));

    private static readonly QuestionDefinition Urgency = QuestionDefinition.Score("urgency", "How urgent is it?", "Low.", "Medium.", "High.");

    private static readonly QuestionSetDefinition AllKinds = new(Refund, Team, Urgency);

    [Fact]
    public async Task EachQuestion_GetsItsOwnCall_AndItsOwnAnswer_WhateverOrderTheCallsFinishIn()
    {
        // The calls finish last question first: each waits for the one after it. Asked one by one, the first would wait forever.
        var urgencyDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var teamDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient(async (messages, _, cancellationToken) =>
        {
            var asked = Asked(messages);
            if (asked == Urgency)
            {
                urgencyDone.SetResult();
                return Named(ChatCompletions.WithTopLogprobs(("C", Ln(0.7)), ("B", Ln(0.2)), ("A", Ln(0.1))), "model-urgency");
            }

            if (asked == Team)
            {
                await urgencyDone.Task.WaitAsync(Bound, cancellationToken);
                teamDone.SetResult();
                return Named(ChatCompletions.WithTopLogprobs(("B", Ln(0.6)), ("A", Ln(0.3)), ("C", Ln(0.1))), "model-team");
            }

            await teamDone.Task.WaitAsync(Bound, cancellationToken);
            return Named(ChatCompletions.WithTopLogprobs(("A", Ln(0.9)), ("B", Ln(0.1))), "model-refund");
        });
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });

        var result = await client.EvaluateAsync(new DecisionRequest(AllKinds, State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(3, chat.Calls.Count);
        var answers = result.Value.Answers;
        Assert.Equal("refund", answers[0].Key);
        Assert.Equal("team", answers[1].Key);
        Assert.Equal("urgency", answers[2].Key);
        Assert.Equal(0.9, answers[0].Probability, 6);
        Assert.Equal(1, answers[1].ChosenIndex);
        Assert.Equal(2, answers[2].ChosenIndex);

        // The refund call finished last, but it is the first question, so its model is the response's.
        Assert.Equal("model-refund", result.Value.Model);
    }

    [Fact]
    public async Task Usage_IsTheSumOverTheCalls_AndModelIsTheFirstResponses()
    {
        var chat = new ScriptedChatClient((messages, _) =>
        {
            var asked = Asked(messages);
            var (model, input, output) = asked == Refund ? ("model-refund", 100, 1) : asked == Team ? ("model-team", 120, 1) : ("model-urgency", 130, 1);
            var response = ChatCompletions.WithTopLogprobs(("A", Ln(0.6)), ("B", Ln(0.4)));
            response.ModelId = model;
            response.Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output };
            return response;
        });
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });

        var result = await client.EvaluateAsync(new DecisionRequest(AllKinds, State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(result.Value.Usage);
        Assert.Equal(350, result.Value.Usage.InputTokens);
        Assert.Equal(3, result.Value.Usage.OutputTokens);
        Assert.Null(result.Value.Usage.Cost);
        Assert.Equal("model-refund", result.Value.Model);
    }

    [Fact]
    public async Task Usage_CountsTheLogprobCall_BeforeAnAutoFallback()
    {
        var chat = new ScriptedChatClient((_, options) =>
        {
            var verbalized = options?.ResponseFormat is ChatResponseFormatJson;
            var response = ChatCompletions.WithoutLogprobs(verbalized ? """{"A":0.7,"B":0.2,"C":0.1}""" : "A");
            response.Usage = verbalized
                ? new UsageDetails { InputTokenCount = 90, OutputTokenCount = 20 }
                : new UsageDetails { InputTokenCount = 80, OutputTokenCount = 1 };
            return response;
        });
        using var client = chat.AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, chat.Calls.Count);
        Assert.Equal(170, result.Value.Usage!.InputTokens);
        Assert.Equal(21, result.Value.Usage.OutputTokens);
    }

    [Fact]
    public async Task Usage_IsNull_WhenNoResponseReportsAny()
    {
        using var client = Logprobs(new ScriptedChatClient((_, _) => ChatCompletions.WithTopLogprobs(("A", Ln(0.6)), ("B", Ln(0.4)))));

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value.Usage);
    }

    [Theory]
    [InlineData(null, "options-model")]
    [InlineData("request-model", "request-model")]
    public async Task Model_IsTheRequestedOne_WhenTheResponseNamesNone(string? requestModel, string expected)
    {
        using var client = new ScriptedChatClient((_, _) => ChatCompletions.WithTopLogprobs(("A", Ln(0.6)), ("B", Ln(0.4))))
            .AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs, Model = "options-model" });

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State) { Model = requestModel });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(expected, result.Value.Model);
    }

    [Fact]
    public async Task TypedEvaluation_AnswersAGeneratedRecord()
    {
        // The record asks the same questions as Refund, Team and Urgency, so Asked tells its calls apart too.
        var chat = new ScriptedChatClient((messages, _) =>
        {
            var asked = Asked(messages);
            return asked == Refund ? ChatCompletions.WithTopLogprobs(("A", Ln(0.8)), ("B", Ln(0.2)))
                : asked == Team ? ChatCompletions.WithTopLogprobs(("A", Ln(0.1)), ("B", Ln(0.9)))
                : ChatCompletions.WithTopLogprobs(("A", Ln(0.1)), ("B", Ln(0.2)), ("C", Ln(0.7)));
        });
        using var client = Logprobs(chat);

        var result = await client.EvaluateAsync<TicketTriage>("I was charged twice.");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(3, chat.Calls.Count);
        Assert.True(result.Value.Refund.Value);
        Assert.Equal(0.8, result.Value.Refund.Probability, 6);
        Assert.Equal(TriageTeam.Shipping, result.Value.Team.Value);
        Assert.Equal(TriageUrgency.High, result.Value.Urgency.Value);
    }

    [Fact]
    public async Task ThePipeline_RecordsEachAnswersProbabilitySource()
    {
        // The Noul has logprobs; the Choice has none, so Auto asks it again, verbalized.
        var chat = new ScriptedChatClient((messages, options) => Asked(messages) == Refund
            ? ChatCompletions.WithTopLogprobs(("A", Ln(0.8)), ("B", Ln(0.2)))
            : ChatCompletions.WithoutLogprobs(options?.ResponseFormat is ChatResponseFormatJson ? """{"A":0.7,"B":0.2,"C":0.1}""" : "A"));
        var sink = new InMemoryDecisionRecordSink();
        await using var recorder = new DecisionRecorder(sink);
        using var client = new DecisionClientBuilder(chat.AsDecisionClient()).UseRecording(recorder).Build();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund, Team), State));
        await recorder.FlushAsync().AsTask().WaitAsync(Bound);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var answers = Assert.Single(sink.Records).Record.Answers;
        Assert.Equal(ProbabilitySource.Logprobs, answers[0].ProbabilitySource);
        Assert.Equal(ProbabilitySource.Verbalized, answers[1].ProbabilitySource);
    }

    // The question a call asks, read from the user message's first line.
    private static QuestionDefinition Asked(IReadOnlyList<ChatMessage> messages)
    {
        var text = messages[1].Text;
        return text.StartsWith("Question: " + Refund.Instructions.ToString(), StringComparison.Ordinal) ? Refund
            : text.StartsWith("Question: " + Team.Instructions.ToString(), StringComparison.Ordinal) ? Team
            : Urgency;
    }

    private static ChatResponse Named(ChatResponse response, string model)
    {
        response.ModelId = model;
        return response;
    }

    private static float Ln(double p) => (float)Math.Log(p);

    private static ChatDecisionClient Logprobs(ScriptedChatClient chat)
        => chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });
}

public enum TriageTeam
{
    [Criterion("Charges, refunds and invoices.")]
    Billing,

    [Criterion("Deliveries and returns.")]
    Shipping,
}

public enum TriageUrgency
{
    [Level("Can wait.")]
    Low,

    [Level("Soon.")]
    Medium,

    [Level("Right now.")]
    High,
}

/// <summary>A generated question set for the typed evaluation test.</summary>
[Questions]
public partial record TicketTriage
{
    [Noul("Does the customer ask for a refund?")]
    public partial Noul Refund { get; }

    [Choice("Which team handles it?")]
    public partial Choice<TriageTeam> Team { get; }

    [Score("How urgent is it?")]
    public partial Score<TriageUrgency> Urgency { get; }
}
