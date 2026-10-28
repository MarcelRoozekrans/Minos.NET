using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI.Tests;

public sealed class ParallelCallsTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static readonly DecisionContent State = DecisionContent.FromString("I was charged twice.");

    // Five Noul questions, told apart by their instructions. Question i answers yes with the probability Yes(i),
    // defined below the tests.
    private static readonly QuestionDefinition[] Questions =
        [.. Enumerable.Range(0, 5).Select(i => QuestionDefinition.Noul("q" + i, "Question number " + i + "?"))];

    private static readonly QuestionSetDefinition FiveQuestions = new(Questions);

    [Fact]
    public async Task AtMostMaxParallelCalls_AreInFlight_AndTheAnswersKeepDefinitionOrder()
    {
        var gated = new GatedChat();
        using var client = gated.Chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs, MaxParallelCalls = 2 });

        var evaluation = client.EvaluateAsync(new DecisionRequest(FiveQuestions, State)).AsTask();
        await gated.FinishLatestFirstAsync(inFlight: 2);
        var result = await evaluation.WaitAsync(Bound);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, gated.MaxInFlight);
        Assert.Equal(5, gated.Chat.Calls.Count);
        Assert.Equal(Questions.Length, result.Value.Answers.Count);
        for (var i = 0; i < Questions.Length; i++)
        {
            Assert.Equal("q" + i, result.Value.Answers[i].Key);
            Assert.Equal(Yes(i), result.Value.Answers[i].Probability, 6);
        }
    }

    [Fact]
    public async Task WithoutMaxParallelCalls_EveryQuestionIsAskedAtOnce()
    {
        var gated = new GatedChat();
        using var client = gated.Chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });

        var evaluation = client.EvaluateAsync(new DecisionRequest(FiveQuestions, State)).AsTask();

        // All five start before any finishes.
        var started = new List<StartedCall>();
        for (var i = 0; i < Questions.Length; i++)
        {
            started.Add(await gated.NextStartedAsync());
        }

        started.ForEach(call => call.Gate.SetResult());
        var result = await evaluation.WaitAsync(Bound);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(5, gated.MaxInFlight);
    }

    [Fact]
    public async Task AnAutoFallback_UsesItsQuestionsSlot()
    {
        // Each logprob call has no logprobs, so Auto asks every question again, verbalized.
        var gated = new GatedChat(verbalizedFallback: true);
        using var client = gated.Chat.AsDecisionClient(new ChatDecisionClientOptions { MaxParallelCalls = 2 });

        var evaluation = client.EvaluateAsync(new DecisionRequest(FiveQuestions, State)).AsTask();
        await gated.FinishLatestFirstAsync(inFlight: 2);
        var result = await evaluation.WaitAsync(Bound);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(10, gated.Chat.Calls.Count);
        Assert.Equal(2, gated.MaxInFlight);
        foreach (var answer in result.Value.Answers)
        {
            Assert.Equal(ProbabilitySource.Verbalized, answer.ProbabilitySource);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxParallelCalls_BelowOne_IsRejected(int value)
    {
        var chat = new ScriptedChatClient((_, _) => throw new InvalidOperationException("No call is expected."));

        var exception = Assert.Throws<ArgumentException>(() => chat.AsDecisionClient(new ChatDecisionClientOptions { MaxParallelCalls = value }));

        Assert.Equal("options", exception.ParamName);
        Assert.Contains("MaxParallelCalls", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailure_WhileOthersWaitForASlot_IsTheResult_AndTheWaitingQuestionsAreNeverAsked()
    {
        // The test fails the first call's gate itself, so without a limit every question would already be asked by then.
        var gated = new GatedChat();
        using var client = gated.Chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs, MaxParallelCalls = 1 });

        var evaluation = client.EvaluateAsync(new DecisionRequest(FiveQuestions, State)).AsTask();
        (await gated.NextStartedAsync()).Gate.SetException(new HttpRequestException("Connection refused."));
        var result = await evaluation.WaitAsync(Bound);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Network, result.Error.Kind);
        Assert.Single(gated.Chat.Calls);
    }

    [Fact]
    public async Task CallerCancellation_WhileWaitingForASlot_Throws()
    {
        // The call in flight ignores cancellation, so only the waiting questions see it.
        var gated = new GatedChat(observeCancellation: false);
        using var client = gated.Chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs, MaxParallelCalls = 1 });
        using var cancellation = new CancellationTokenSource();

        var evaluation = client.EvaluateAsync(new DecisionRequest(FiveQuestions, State), cancellation.Token).AsTask();
        var first = await gated.NextStartedAsync();
        await cancellation.CancelAsync();
        first.Gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => evaluation.WaitAsync(Bound));
        Assert.Single(gated.Chat.Calls);
    }

    private static double Yes(int index) => 0.5 + (index / 10.0);

    private static int IndexOf(IReadOnlyList<ChatMessage> messages)
        => Array.FindIndex(Questions, question => messages[1].Text.StartsWith("Question: " + question.Instructions.ToString(), StringComparison.Ordinal));

    /// <summary>A call the gated chat client has started: the gate that lets it finish, and whether it is a verbalized one.</summary>
    private readonly record struct StartedCall(TaskCompletionSource Gate, bool Verbalized);

    /// <summary>
    /// A chat client whose every call waits for a gate the test opens, and that counts the calls in flight. Each call
    /// announces itself on <see cref="NextStartedAsync"/> when it starts.
    /// </summary>
    private sealed class GatedChat
    {
        private readonly Channel<StartedCall> _started = Channel.CreateUnbounded<StartedCall>();
        private readonly bool _verbalizedFallback;
        private int _inFlight;
        private int _maxInFlight;

        public GatedChat(bool observeCancellation = true, bool verbalizedFallback = false)
        {
            _verbalizedFallback = verbalizedFallback;
            Chat = new ScriptedChatClient(async (messages, options, cancellationToken) =>
            {
                var now = Interlocked.Increment(ref _inFlight);
                InterlockedMax(ref _maxInFlight, now);
                try
                {
                    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _started.Writer.TryWrite(new StartedCall(gate, options?.ResponseFormat is ChatResponseFormatJson));
                    await gate.Task.WaitAsync(Bound, observeCancellation ? cancellationToken : CancellationToken.None);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }

                var yes = Yes(IndexOf(messages));
                if (!verbalizedFallback)
                {
                    return ChatCompletions.WithTopLogprobs(("A", (float)Math.Log(yes)), ("B", (float)Math.Log(1 - yes)));
                }

                return options?.ResponseFormat is ChatResponseFormatJson
                    ? ChatCompletions.WithoutLogprobs("{\"A\":0.6,\"B\":0.4}")
                    : ChatCompletions.WithoutLogprobs("A");
            });
        }

        public ScriptedChatClient Chat { get; }

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        /// <summary>Waits for the next call to start.</summary>
        public async Task<StartedCall> NextStartedAsync()
        {
            using var timeout = new CancellationTokenSource(Bound);
            return await _started.Reader.ReadAsync(timeout.Token);
        }

        /// <summary>
        /// Waits for <paramref name="inFlight"/> calls to start, then finishes the latest call in flight, one at a time,
        /// so the calls finish in another order than the questions'. Each finished call is followed by the next one to
        /// start, if any: its question's verbalized call, or another question's first call.
        /// </summary>
        public async Task FinishLatestFirstAsync(int inFlight)
        {
            var calls = new List<StartedCall>();
            for (var i = 0; i < inFlight; i++)
            {
                calls.Add(await NextStartedAsync());
            }

            var questionsStarted = inFlight;
            while (calls.Count > 0)
            {
                var latest = calls[^1];
                calls.RemoveAt(calls.Count - 1);
                latest.Gate.SetResult();

                var fallsBack = _verbalizedFallback && !latest.Verbalized;
                if (fallsBack || questionsStarted < Questions.Length)
                {
                    var next = await NextStartedAsync();
                    questionsStarted += next.Verbalized ? 0 : 1;
                    calls.Add(next);
                }
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current)
                {
                    return;
                }

                current = seen;
            }
        }
    }
}
