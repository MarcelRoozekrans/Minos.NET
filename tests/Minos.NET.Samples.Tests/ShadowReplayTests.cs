using System.Collections.Concurrent;
using System.Diagnostics;
using Minos.Evaluation;
using Minos.Recording;
using Minos.Samples.ShadowReplay;
using ZeroAlloc.Results;

namespace Minos.Samples.Tests;

/// <summary>The replay loop: every dataset item is decided by the primary, labelled, and shadowed by the candidate before the next.</summary>
public sealed class ShadowReplayTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    // Three items in the Banking77 shape: one Choice question and two Noul questions each.
    private const string Questions = """
        "questions":{"intent":{"type":"choice","instructions":"Which topic is this customer message about?","criteria":{"card_arrival":{"description":"Where a card is."},"lost_or_stolen_card":{"description":"A lost card."},"change_pin":{"description":"A new PIN."}}},"card_not_arrived":{"type":"noul","instructions":"Is the message about the arrival of a card?","criteria":{"true":"It is.","false":"It is not."}},"about_a_payment":{"type":"noul","instructions":"Is the message about a card payment?","criteria":{"true":"It is.","false":"It is not."}}}
        """;

    private static readonly string Jsonl = string.Join<string>(
        "\n",
        [
            $$"""{"id":"b77-card_arrival-01","state":"How do I locate my card?",{{Questions}},"expected":{"intent":"card_arrival","card_not_arrived":true,"about_a_payment":false},"source":"banking77/test#1","version":1}""",
            $$"""{"id":"b77-lost_or_stolen_card-01","state":"I think my card was stolen.",{{Questions}},"expected":{"intent":"lost_or_stolen_card","card_not_arrived":false,"about_a_payment":false},"source":"banking77/test#2","version":1}""",
            $$"""{"id":"b77-change_pin-01","state":"How do I set a new PIN?",{{Questions}},"expected":{"intent":"change_pin","card_not_arrived":false,"about_a_payment":false},"source":"banking77/test#3","version":1}""",
        ]);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"shadow-replay-{Guid.NewGuid():N}.jsonl");
    private readonly StringWriter _log = new();

    public void Dispose()
    {
        File.Delete(_path);
        _log.Dispose();
    }

    private static DecisionDataset Dataset(string jsonl)
    {
        var parsed = DecisionDataset.Parse(jsonl);
        Assert.True(parsed.IsSuccess);
        return parsed.Value;
    }

    private static DecisionDataset Dataset() => Dataset(Jsonl);

    private async Task<List<RecordedDecision>> ReadAsync()
    {
        var decisions = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(_path, CancellationToken.None))
        {
            decisions.Add(decision);
        }

        return decisions;
    }

    [Fact]
    public void ConfirmedAnswers_MapsEveryExpectedAnswer()
    {
        var item = Dataset()[0];

        var answers = ShadowReplayLoop.ConfirmedAnswers(item);

        Assert.Equal(3, answers.Count);
        Assert.Equal(ConfirmedAnswer.ForChoice("card_arrival"), answers["intent"]);
        Assert.Equal(ConfirmedAnswer.ForNoul(true), answers["card_not_arrived"]);
        Assert.Equal(ConfirmedAnswer.ForNoul(false), answers["about_a_payment"]);

        var scored = Dataset("""{"id":"u-1","state":"My card was stolen.","questions":{"urgency":{"type":"score","instructions":"How urgent?","criteria":["Routine","Soon","Urgent"]}},"expected":{"urgency":2},"version":1}""")[0];
        Assert.Equal(ConfirmedAnswer.ForScore(2), Assert.Single(ShadowReplayLoop.ConfirmedAnswers(scored)).Value);
    }

    [Fact]
    public async Task Replay_RecordsEveryItemWithItsIdAndOutcome()
    {
        var dataset = Dataset();
        using var candidate = new FakeCandidate();

        var summary = await ShadowReplayLoop.RunAsync(new FakePrimary(), candidate, dataset, _path, _log, CancellationToken.None);

        Assert.Equal(new ReplaySummary(3, 3, 0, 3, 0), summary);
        var decisions = await ReadAsync();
        Assert.Equal(dataset.Select(item => item.Id), decisions.Select(decision => decision.Record.DecisionId));
        for (var i = 0; i < dataset.Count; i++)
        {
            var outcome = Assert.IsType<ConfirmedOutcome>(Assert.Single(decisions[i].Outcomes));
            Assert.Equal("banking77-label", outcome.Source);
            var expected = ShadowReplayLoop.ConfirmedAnswers(dataset[i]);
            Assert.Equal(expected.Count, outcome.Answers.Count);
            foreach (var (key, answer) in expected)
            {
                Assert.Equal(answer, outcome.Answers[key]);
            }

            Assert.NotNull(decisions[i].Record.Shadow);
        }
    }

    [Fact]
    public async Task Replay_WaitsForEachShadowBeforeTheNextItem()
    {
        var events = new ConcurrentQueue<string>();
        var primary = new FakePrimary { OnCall = (n, _) => events.Enqueue($"primary-start-{n}") };
        using var candidate = new FakeCandidate { Delay = TimeSpan.FromMilliseconds(300), OnEnd = n => events.Enqueue($"candidate-end-{n}") };

        var summary = await ShadowReplayLoop.RunAsync(primary, candidate, Dataset(), _path, _log, CancellationToken.None);

        var order = events.ToList();
        for (var n = 1; n < 3; n++)
        {
            Assert.True(order.IndexOf($"candidate-end-{n}") >= 0, string.Join(", ", order));
            Assert.True(order.IndexOf($"candidate-end-{n}") < order.IndexOf($"primary-start-{n + 1}"), string.Join(", ", order));
        }

        Assert.Equal(new ReplaySummary(3, 3, 0, 3, 0), summary);
        Assert.All(await ReadAsync(), decision => Assert.NotNull(decision.Record.Shadow));
    }

    [Fact]
    public async Task Replay_DoesNotMissAShadowThatFinishesBeforeTheWait()
    {
        using var candidate = new FakeCandidate();

        var summary = await ShadowReplayLoop.RunAsync(new FakePrimary(), candidate, Dataset(), _path, _log, CancellationToken.None);

        Assert.True(candidate.AnsweredSynchronously);
        Assert.Equal(3, summary.Shadowed);
        Assert.Equal(0, summary.WaitsRunOut);
        Assert.All(await ReadAsync(), decision => Assert.NotNull(decision.Record.Shadow));
    }

    [Fact]
    public async Task Replay_EndsTheWaitWhenTheCandidateFails()
    {
        using var candidate = new FakeCandidate { Failure = DecisionErrorKind.Server };
        var clock = Stopwatch.StartNew();

        var summary = await ShadowReplayLoop.RunAsync(new FakePrimary(), candidate, Dataset(), _path, _log, CancellationToken.None);

        Assert.True(clock.Elapsed < Bound, $"The run took {clock.Elapsed}.");
        Assert.Equal(new ReplaySummary(3, 3, 0, 3, 0), summary);
        Assert.All(await ReadAsync(), decision => Assert.Equal(DecisionErrorKind.Server, decision.Record.Shadow!.ErrorKind));
    }

    [Fact]
    public async Task Replay_DoesNotWaitWhenThePrimaryFails()
    {
        var dataset = Dataset();
        using var candidate = new FakeCandidate();
        var clock = Stopwatch.StartNew();

        var summary = await ShadowReplayLoop.RunAsync(new FakePrimary { FailCall = 2 }, candidate, dataset, _path, _log, CancellationToken.None);

        Assert.True(clock.Elapsed < Bound, $"The run took {clock.Elapsed}.");
        Assert.Equal(new ReplaySummary(3, 2, 1, 2, 0), summary);
        Assert.Contains($"{dataset[1].Id}: primary failed (Network)", _log.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(dataset[1].State.ToString(), _log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_KeepsTheRecordsWrittenBeforeACancel()
    {
        var dataset = Dataset();
        using var cancel = new CancellationTokenSource();

        // The second candidate call starts only after the first shadow was written; it cancels the run and then hangs,
        // ignoring its token, so the run must not wait for it either.
        using var candidate = new FakeCandidate
        {
            HangFrom = 2,
            OnStart = n =>
            {
                if (n == 2)
                {
                    cancel.Cancel();
                }
            },
        };
        var clock = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ShadowReplayLoop.RunAsync(new FakePrimary(), candidate, dataset, _path, _log, cancel.Token));

        Assert.True(clock.Elapsed < Bound, $"The run took {clock.Elapsed}.");
        var first = (await ReadAsync()).Single(decision => string.Equals(decision.Record.DecisionId, dataset[0].Id, StringComparison.Ordinal));
        Assert.NotNull(first.Record.Shadow);
        Assert.IsType<ConfirmedOutcome>(Assert.Single(first.Outcomes));
    }

    [Fact]
    public async Task Replay_BuildsAShadowReportOverEveryItem()
    {
        using var candidate = new FakeCandidate();
        await ShadowReplayLoop.RunAsync(new FakePrimary(), candidate, Dataset(), _path, _log, CancellationToken.None);

        var report = await ShadowReport.CreateAsync(DecisionRecordFile.ReadAsync(_path, CancellationToken.None));

        Assert.Equal(3, report.DecisionsShadowed);
    }

    // Answers each question with the probability that matches its expected answer.
    private static DecisionResponse Answer(DecisionRequest request, Func<QuestionDefinition, int, bool> right, string model)
    {
        var answers = new List<QuestionAnswer>();
        foreach (var question in request.Definition.Questions)
        {
            if (question.Kind == QuestionKind.Noul)
            {
                answers.Add(QuestionAnswer.Noul(right(question, 1) ? 0.9 : 0.1));
                continue;
            }

            var options = question.Options.Count;
            var chosen = 0;
            while (chosen < options - 1 && !right(question, chosen))
            {
                chosen++;
            }

            var probabilities = new double[options];
            for (var index = 0; index < options; index++)
            {
                probabilities[index] = index == chosen ? 0.8 : 0.2 / (options - 1);
            }

            answers.Add(question.Kind == QuestionKind.Choice
                ? QuestionAnswer.Choice(chosen, 0.8, probabilities)
                : QuestionAnswer.Score(chosen, chosen, 0.8, probabilities));
        }

        return new DecisionResponse(request.Definition, answers, model);
    }

    /// <summary>The primary: answers every question with the item's expected answer, found by its state.</summary>
    private sealed class FakePrimary : IDecisionClient
    {
        private readonly Dictionary<string, DatasetItem> _items = Dataset().ToDictionary(item => item.State.ToString(), StringComparer.Ordinal);
        private int _calls;

        public Action<int, CancellationToken>? OnCall { get; init; }

        public int? FailCall { get; init; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref _calls);
            OnCall?.Invoke(n, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (n == FailCall)
            {
                return ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Network, "The network failed.")));
            }

            var expected = _items[request.State.ToString()].Expected;
            var response = Answer(request, (question, index) => question.Kind == QuestionKind.Noul
                ? expected[question.Key].Noul
                : string.Equals(question.Options[index].Key, expected[question.Key].ChoiceKey, StringComparison.Ordinal), "primary-model");
            return ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Success(response));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>The candidate: answers at once, after a delay, with an error, or hangs from a call on, ignoring its token.</summary>
    private sealed class FakeCandidate : IDecisionClient
    {
        private readonly TaskCompletionSource _hang = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        private int _asynchronous;

        public TimeSpan Delay { get; init; }

        public DecisionErrorKind? Failure { get; init; }

        public int? HangFrom { get; init; }

        public Action<int>? OnStart { get; init; }

        public Action<int>? OnEnd { get; init; }

        public bool AnsweredSynchronously => _calls > 0 && Volatile.Read(ref _asynchronous) == 0;

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref _calls);
            OnStart?.Invoke(n);
            if (n >= HangFrom || Delay > TimeSpan.Zero)
            {
                Interlocked.Exchange(ref _asynchronous, 1);
                return LaterAsync(request, n, cancellationToken);
            }

            return ValueTask.FromResult(Result(request, n));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        // Lets a hung call end, so nothing outlives the test.
        public void Dispose() => _hang.TrySetResult();

        private async ValueTask<Result<DecisionResponse, DecisionError>> LaterAsync(DecisionRequest request, int n, CancellationToken cancellationToken)
        {
            if (n >= HangFrom)
            {
                await _hang.Task.ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }

            return Result(request, n);
        }

        private Result<DecisionResponse, DecisionError> Result(DecisionRequest request, int n)
        {
            OnEnd?.Invoke(n);
            return Failure is { } kind
                ? Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, "The candidate failed."))
                : Result<DecisionResponse, DecisionError>.Success(Answer(request, (_, index) => index == 0, "candidate-model"));
        }
    }
}
