using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;
using Fake = Minos.Tests.EscalationTests.Fake;

namespace Minos.Tests;

/// <summary>The escalation stage's time check, the deadline it stamps on its requests, the deadline's cancellation, and disposal.</summary>
public sealed class EscalationTimeTests
{
    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    // department at 0.55 is unsure by the default threshold of 0.6; the other two are confident.
    private static readonly Dictionary<string, QuestionAnswer> UnsurePrimary = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.8, [0.1, 0.8, 0.1]),
    };

    private static readonly DateTimeOffset Start = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static EscalationOptions Options(Action<EscalationOptions>? configure = null)
    {
        var options = new EscalationOptions { TargetName = "hosted" };
        configure?.Invoke(options);
        return options;
    }

    // A primary that answers UnsurePrimary, records each request, and moves the clock on by `takes` before answering.
    private static Fake Primary(FakeTimeProvider time, List<DecisionRequest> seen, TimeSpan takes = default)
        => new(request => Fake.Respond(request.Definition, UnsurePrimary), request =>
        {
            seen.Add(request);
            time.Advance(takes);
        });

    [Fact]
    public async Task No_time_check_without_a_deadline_or_budget()
    {
        var time = new FakeTimeProvider(Start);
        List<DecisionRequest> primarySeen = [], targetSeen = [];
        using var client = new EscalatingDecisionClient(
            Primary(time, primarySeen, TimeSpan.FromHours(1)),
            Fake.Answering(TargetAnswers, targetSeen.Add),
            Options(o => o.ExpectedLatency = TimeSpan.FromHours(1)),
            timeProvider: time);
        var request = new DecisionRequest(Triage, State);

        var response = (await client.EvaluateAsync(request)).Value;

        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[1].Reason);
        Assert.Same(request, Assert.Single(primarySeen));
        Assert.Null(Assert.Single(targetSeen).Deadline);
    }

    [Fact]
    public async Task Time_left_below_ExpectedLatency_skips_with_NoTimeLeft()
    {
        // 5 s to the deadline at entry; the primary takes 4 s, which leaves 1 s for a target expected to take 2 s.
        var time = new FakeTimeProvider(Start);
        var targetCalls = 0;
        using var client = new EscalatingDecisionClient(
            Primary(time, [], TimeSpan.FromSeconds(4)),
            new Fake(_ => null, _ => targetCalls++),
            Options(o => o.ExpectedLatency = TimeSpan.FromSeconds(2)),
            timeProvider: time);

        // A definition of its own, so no other test has cached a subset of it.
        var triage = QuestionSets.TriageDefinition();

        var response = (await client.EvaluateAsync(new DecisionRequest(triage, State) { Deadline = Start.AddSeconds(5) })).Value;

        Assert.Equal(0, targetCalls);
        Assert.Equal(
            [
                new QuestionRoute("is_urgent", "primary", RouteReason.Confident, 0.97, null),
                new QuestionRoute("department", "primary", RouteReason.NoTimeLeft, 0.55, null),
                new QuestionRoute("severity", "primary", RouteReason.Confident, 0.9, null),
            ],
            response.Path!.Routes);
        Assert.Equal("primary", Assert.Single(response.Path.Calls).ClientName);
        Assert.Equal(0, response.Answers[1].ChosenIndex);
        Assert.Equal(0, EscalationMerge.CachedSubsetCount(triage));
    }

    [Fact]
    public async Task Time_left_equal_to_ExpectedLatency_escalates()
    {
        var time = new FakeTimeProvider(Start);
        using var client = new EscalatingDecisionClient(
            Primary(time, [], TimeSpan.FromSeconds(3)),
            Fake.Answering(TargetAnswers),
            Options(o => o.ExpectedLatency = TimeSpan.FromSeconds(2)),
            timeProvider: time);

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(5) })).Value;

        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[1].Reason);
    }

    [Fact]
    public async Task The_request_deadline_wins_over_Budget()
    {
        // The budget alone would leave 10 s; the request's deadline leaves 1 s, less than the 2 s the target needs.
        var time = new FakeTimeProvider(Start);
        List<DecisionRequest> primarySeen = [];
        var targetCalls = 0;
        using var client = new EscalatingDecisionClient(
            Primary(time, primarySeen),
            new Fake(_ => null, _ => targetCalls++),
            Options(o =>
            {
                o.Budget = TimeSpan.FromSeconds(10);
                o.ExpectedLatency = TimeSpan.FromSeconds(2);
            }),
            timeProvider: time);
        var request = new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(1) };

        var response = (await client.EvaluateAsync(request)).Value;

        Assert.Equal(0, targetCalls);
        Assert.Equal(RouteReason.NoTimeLeft, response.Path!.Routes[1].Reason);
        Assert.Same(request, Assert.Single(primarySeen));
    }

    [Theory]
    [InlineData(2, RouteReason.Escalated)]
    [InlineData(4, RouteReason.NoTimeLeft)]
    public async Task Budget_counts_from_stage_entry(int primarySeconds, RouteReason expected)
    {
        // The clock already reads 1 minute past Start when the call enters the stage, so the deadline is entry + 5 s.
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(1));
        var entry = time.GetUtcNow();
        List<DecisionRequest> primarySeen = [];
        using var client = new EscalatingDecisionClient(
            Primary(time, primarySeen, TimeSpan.FromSeconds(primarySeconds)),
            Fake.Answering(TargetAnswers),
            Options(o =>
            {
                o.Budget = TimeSpan.FromSeconds(5);
                o.ExpectedLatency = TimeSpan.FromSeconds(2);
            }),
            timeProvider: time);

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        Assert.Equal(expected, response.Path!.Routes[1].Reason);
        Assert.Equal(entry.AddSeconds(5), Assert.Single(primarySeen).Deadline);
    }

    [Fact]
    public async Task The_deadline_is_stamped_on_the_inner_and_subset_requests()
    {
        var time = new FakeTimeProvider(Start);
        List<DecisionRequest> primarySeen = [], targetSeen = [];
        using var client = new EscalatingDecisionClient(
            Primary(time, primarySeen, TimeSpan.FromSeconds(1)),
            Fake.Answering(TargetAnswers, targetSeen.Add),
            Options(o => o.Budget = TimeSpan.FromSeconds(5)),
            timeProvider: time);
        var request = new DecisionRequest(Triage, State) { Model = "local-2b" };

        var response = (await client.EvaluateAsync(request)).Value;

        var inner = Assert.Single(primarySeen);
        var subset = Assert.Single(targetSeen);
        Assert.Equal(Start.AddSeconds(5), inner.Deadline);
        Assert.Equal(Start.AddSeconds(5), subset.Deadline);
        Assert.Equal(request, inner);
        Assert.Equal("local-2b", inner.Model);
        Assert.Null(request.Deadline);
        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[1].Reason);
    }

    [Fact]
    public async Task A_request_deadline_reaches_the_subset_request()
    {
        var time = new FakeTimeProvider(Start);
        List<DecisionRequest> targetSeen = [];
        using var client = new EscalatingDecisionClient(
            Primary(time, []),
            Fake.Answering(TargetAnswers, targetSeen.Add),
            Options(),
            timeProvider: time);

        await client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(5) });

        Assert.Equal(Start.AddSeconds(5), Assert.Single(targetSeen).Deadline);
    }

    [Fact]
    public async Task A_deadline_already_past_still_calls_the_primary()
    {
        var time = new FakeTimeProvider(Start);
        List<DecisionRequest> primarySeen = [];
        var targetCalls = 0;
        using var client = new EscalatingDecisionClient(
            Primary(time, primarySeen),
            new Fake(_ => null, _ => targetCalls++),
            Options(),
            timeProvider: time);

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(-1) })).Value;

        Assert.Single(primarySeen);
        Assert.Equal(0, targetCalls);
        Assert.Equal(
            [RouteReason.Confident, RouteReason.NoTimeLeft, RouteReason.Confident],
            response.Path!.Routes.Select(r => r.Reason));
        Assert.Single(response.Path.Calls);
    }

    [Fact]
    public async Task A_slow_target_is_cancelled_at_the_deadline_as_TargetFailed()
    {
        var time = new FakeTimeProvider(Start);
        var target = new WaitingClient();
        using var client = new EscalatingDecisionClient(Primary(time, []), target, Options(), timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(5) }).AsTask();
        await target.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(target.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromSeconds(1));
        var response = (await call.WaitAsync(TimeSpan.FromSeconds(10))).Value;

        Assert.Equal(new QuestionRoute("department", "primary", RouteReason.TargetFailed, 0.55, null), response.Path!.Routes[1]);
        Assert.Equal(0, response.Answers[1].ChosenIndex);
        Assert.Equal(
            [new DecisionCall("primary", null, null, IsCached: false), new DecisionCall("hosted", null, null, IsCached: false)],
            response.Path.Calls);
    }

    [Fact]
    public async Task A_target_failure_result_at_the_deadline_is_TargetFailed()
    {
        // A target that turns the deadline's cancellation into a failure result, as a transport does with a time-out.
        var time = new FakeTimeProvider(Start);
        var target = new WaitingClient(failOnCancel: true);
        using var client = new EscalatingDecisionClient(Primary(time, []), target, Options(), timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(5) }).AsTask();
        await target.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(5));
        var response = (await call.WaitAsync(TimeSpan.FromSeconds(10))).Value;

        Assert.Equal(RouteReason.TargetFailed, response.Path!.Routes[1].Reason);
    }

    [Fact]
    public async Task The_callers_cancellation_throws_OperationCanceledException()
    {
        var time = new FakeTimeProvider(Start);
        var target = new WaitingClient();
        using var cts = new CancellationTokenSource();
        using var client = new EscalatingDecisionClient(Primary(time, []), target, Options(), timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(5) }, cts.Token).AsTask();
        await target.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cts.Token, caught.CancellationToken);
    }

    [Fact]
    public async Task The_callers_cancellation_wins_when_the_deadline_passes_too()
    {
        var time = new FakeTimeProvider(Start);
        using var cts = new CancellationTokenSource();
        var target = new WaitingClient(onStart: () =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            cts.Cancel();
        });
        using var client = new EscalatingDecisionClient(Primary(time, []), target, Options(), timeProvider: time);

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(5) }, cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cts.Token, caught.CancellationToken);
    }

    [Fact]
    public async Task The_callers_cancellation_without_a_deadline_throws()
    {
        var target = new WaitingClient();
        using var cts = new CancellationTokenSource();
        using var client = new EscalatingDecisionClient(Primary(new FakeTimeProvider(Start), []), target, Options());

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask();
        await target.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_deadline_beyond_the_timer_range_still_escalates()
    {
        // A timer cannot run for this long; the stage then leaves the target to the caller's token.
        var time = new FakeTimeProvider(Start);
        using var client = new EscalatingDecisionClient(Primary(time, []), Fake.Answering(TargetAnswers), Options(), timeProvider: time);

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = DateTimeOffset.MaxValue })).Value;

        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[1].Reason);
    }

    [Fact]
    public void Dispose_disposes_the_primary_but_not_the_target()
    {
        var primary = new DisposeCountingClient();
        var target = new DisposeCountingClient();
        var client = new EscalatingDecisionClient(primary, target, Options());

        client.Dispose();

        Assert.Equal(1, primary.Disposals);
        Assert.Equal(0, target.Disposals);
    }

    /// <summary>A target that waits until its token is cancelled, then throws, or returns a time-out failure.</summary>
    private sealed class WaitingClient(bool failOnCancel = false, Action? onStart = null) : IDecisionClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public CancellationToken Token { get; private set; }

        public async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            _started.SetResult();
            onStart?.Invoke();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (failOnCancel)
            {
                return Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Timeout, "timed out"));
            }

            throw new InvalidOperationException("The wait ended without cancellation.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class DisposeCountingClient : IDecisionClient
    {
        public int Disposals { get; private set; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(new DecisionError(DecisionErrorKind.Server, "unused"));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Disposals++;
    }
}
