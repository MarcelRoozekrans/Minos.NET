using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>The fallback stage's time check, the deadline it stamps on its requests, the cut of a step's call, and the caller's cancellation.</summary>
public sealed class FallbackTimeTests
{
    private const string CutMessage = "A fallback step ran past its time limit.";

    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, QuestionAnswer> StepAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.7, [0.2, 0.7, 0.1]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.85, [0.05, 0.85, 0.1]),
    };

    private static FallbackOptions Options(TimeSpan? budget, params FallbackStep[] steps)
    {
        var options = new FallbackOptions { Budget = budget };
        foreach (var step in steps)
        {
            options.Steps.Add(step);
        }

        return options;
    }

    private static FallbackStep Step(string name, IDecisionClient client, double expectedSeconds)
        => new(name, client) { ExpectedLatency = TimeSpan.FromSeconds(expectedSeconds) };

    // A client that fails with Server after moving the clock on by `takes`.
    private static StubDecisionClient FailingAfter(FakeTimeProvider time, TimeSpan takes)
        => new((_, _) =>
        {
            time.Advance(takes);
            return new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed")));
        });

    private static DecisionCall Failed(string name, DecisionErrorKind kind)
        => new(name, null, null, IsCached: false) { Outcome = CallOutcome.Failed, ErrorKind = kind };

    private static DecisionCall Skipped(string name)
        => new(name, null, null, IsCached: false) { Outcome = CallOutcome.NotCalledNoTimeLeft };

    [Fact]
    public async Task No_time_check_without_a_deadline_or_budget()
    {
        var time = new FakeTimeProvider(Start);
        var primary = FailingAfter(time, TimeSpan.FromHours(1));
        CancellationToken seenToken = default;
        var clef = new StubDecisionClient((request, token) =>
        {
            seenToken = token;
            return new(Result<DecisionResponse, DecisionError>.Success(StubDecisionClient.Respond(request.Definition, StepAnswers)));
        });
        using var cts = new CancellationTokenSource();
        using var client = new FallbackDecisionClient(primary, Options(null, Step("clef", clef, 3600)), timeProvider: time);
        var request = new DecisionRequest(Triage, State);

        var result = await client.EvaluateAsync(request, cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Same(request, Assert.Single(primary.Requests));
        Assert.Null(Assert.Single(clef.Requests).Deadline);
        Assert.Equal(cts.Token, seenToken);
    }

    [Fact]
    public async Task A_step_that_does_not_fit_is_skipped_as_NotCalledNoTimeLeft()
    {
        // 5 s at entry; the primary takes 4 s, which leaves 1 s: too little for clef at 2 s, enough for hosted at 0.5 s.
        var time = new FakeTimeProvider(Start);
        var clef = StubDecisionClient.Answering(StepAnswers);
        var hosted = StubDecisionClient.Answering(StepAnswers, "hosted-model");
        using var client = new FallbackDecisionClient(
            FailingAfter(time, TimeSpan.FromSeconds(4)),
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 2), Step("hosted", hosted, 0.5)),
            timeProvider: time);

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        Assert.Equal(0, clef.Calls);
        Assert.Equal(1, hosted.Calls);
        Assert.Equal(
            [Failed("primary", DecisionErrorKind.Server), Skipped("clef"), new DecisionCall("hosted", "hosted-model", null, IsCached: false)],
            response.Path!.Calls);
        Assert.All(response.Path.Routes, route => Assert.Equal("hosted", route.AnsweredBy));
    }

    [Fact]
    public async Task Time_left_equal_to_ExpectedLatency_calls_the_step()
    {
        var time = new FakeTimeProvider(Start);
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            FailingAfter(time, TimeSpan.FromSeconds(3)),
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 2)),
            timeProvider: time);

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, clef.Calls);
    }

    [Fact]
    public async Task All_later_steps_skipped_returns_step_zeros_error()
    {
        // Neither step fits a 5 s budget, so step 0 runs to the deadline; it fails after 1 s, and both steps are skipped.
        var time = new FakeTimeProvider(Start);
        var primaryError = new DecisionError(DecisionErrorKind.Overloaded, "overloaded");
        var primary = new StubDecisionClient((_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return new(Result<DecisionResponse, DecisionError>.Failure(primaryError));
        });
        var clef = StubDecisionClient.Answering(StepAnswers);
        var hosted = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 6), Step("hosted", hosted, 7)),
            timeProvider: time);

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.False(result.IsSuccess);
        Assert.Same(primaryError, result.Error);
        Assert.Equal(0, clef.Calls);
        Assert.Equal(0, hosted.Calls);
    }

    [Fact]
    public async Task The_request_deadline_wins_over_Budget()
    {
        // The budget alone would leave 10 s; the request's deadline leaves 1 s, less than the 2 s clef needs.
        var time = new FakeTimeProvider(Start);
        var primary = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(10), Step("clef", clef, 2)),
            timeProvider: time);
        var request = new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(1) };

        var result = await client.EvaluateAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, clef.Calls);
        Assert.Same(request, Assert.Single(primary.Requests));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, false)]
    public async Task Budget_counts_from_stage_entry(int primarySeconds, bool clefCalled)
    {
        // The clock already reads 1 minute past Start when the call enters the stage, so the deadline is entry + 5 s.
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(1));
        var entry = time.GetUtcNow();
        var primary = FailingAfter(time, TimeSpan.FromSeconds(primarySeconds));
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 2)),
            timeProvider: time);

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal(clefCalled, result.IsSuccess);
        Assert.Equal(clefCalled ? 1 : 0, clef.Calls);
        Assert.Equal(entry.AddSeconds(5), Assert.Single(primary.Requests).Deadline);
    }

    [Fact]
    public async Task The_deadline_is_stamped_on_every_step_request()
    {
        // The request has no deadline: the one Budget resolves at entry reaches every step, step 0 included.
        var time = new FakeTimeProvider(Start);
        var primary = FailingAfter(time, TimeSpan.FromSeconds(1));
        var clef = FailingAfter(time, TimeSpan.FromSeconds(1));
        var hosted = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3), Step("hosted", hosted, 0.3)),
            timeProvider: time);
        var request = new DecisionRequest(Triage, State) { Model = "local-2b", RetryAttempt = 1 };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsSuccess);
        var inner = Assert.Single(primary.Requests);
        Assert.Equal(request with { Deadline = Start.AddSeconds(5) }, inner);
        Assert.Equal(Start.AddSeconds(5), Assert.Single(clef.Requests).Deadline);
        Assert.Equal(Start.AddSeconds(5), Assert.Single(hosted.Requests).Deadline);
        Assert.Null(request.Deadline);
    }

    [Fact]
    public async Task A_request_deadline_is_stamped_on_every_step_request()
    {
        var time = new FakeTimeProvider(Start);
        var deadline = Start.AddSeconds(5);
        var primary = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var clef = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var hosted = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(10), Step("clef", clef, 0.3), Step("hosted", hosted, 0.3)),
            timeProvider: time);
        var request = new DecisionRequest(Triage, State) { Deadline = deadline };

        await client.EvaluateAsync(request);

        Assert.Same(request, Assert.Single(primary.Requests));
        Assert.Equal(deadline, Assert.Single(clef.Requests).Deadline);
        Assert.Equal(deadline, Assert.Single(hosted.Requests).Deadline);
    }

    [Fact]
    public async Task A_hung_step_zero_is_cut_so_the_next_step_fits()
    {
        // A 5 s budget and clef expected at 300 ms: a hung local call is cut at 4.7 s, and clef answers.
        var time = new FakeTimeProvider(Start);
        var primary = new WaitingClient();
        var clef = StubDecisionClient.Answering(StepAnswers, "clef-model");
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3)),
            timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromMilliseconds(4699));
        Assert.False(primary.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var response = (await call.WaitAsync(TimeSpan.FromSeconds(10))).Value;

        Assert.Equal("clef-model", response.Model);
        Assert.Equal(
            [Failed("primary", DecisionErrorKind.Timeout), new DecisionCall("clef", "clef-model", null, IsCached: false)],
            response.Path!.Calls);
    }

    [Fact]
    public async Task The_cut_uses_the_smallest_later_expected_latency()
    {
        // [clef 2 s, hosted 300 ms]: step 0 is cut at deadline - 300 ms, which leaves no time for clef but enough for hosted.
        var time = new FakeTimeProvider(Start);
        var primary = new WaitingClient();
        var clef = StubDecisionClient.Answering(StepAnswers);
        var hosted = StubDecisionClient.Answering(StepAnswers, "hosted-model");
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 2), Step("hosted", hosted, 0.3)),
            timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromMilliseconds(4699));
        Assert.False(primary.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var response = (await call.WaitAsync(TimeSpan.FromSeconds(10))).Value;

        Assert.Equal(0, clef.Calls);
        Assert.Equal(
            [Failed("primary", DecisionErrorKind.Timeout), Skipped("clef"), new DecisionCall("hosted", "hosted-model", null, IsCached: false)],
            response.Path!.Calls);
    }

    [Fact]
    public async Task A_later_step_is_cut_so_the_step_after_it_fits()
    {
        var time = new FakeTimeProvider(Start);
        var clef = new WaitingClient();
        var hosted = StubDecisionClient.Answering(StepAnswers, "hosted-model");
        using var client = new FallbackDecisionClient(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3), Step("hosted", hosted, 1)),
            timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await clef.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromMilliseconds(3999));
        Assert.False(clef.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var response = (await call.WaitAsync(TimeSpan.FromSeconds(10))).Value;

        Assert.Equal(
            [Failed("primary", DecisionErrorKind.Server), Failed("clef", DecisionErrorKind.Timeout), new DecisionCall("hosted", "hosted-model", null, IsCached: false)],
            response.Path!.Calls);
    }

    [Fact]
    public async Task A_cut_already_past_runs_the_step_to_the_deadline()
    {
        // Clef needs 6 s of a 5 s budget, so no cut leaves it room: step 0 runs to the deadline, and clef is skipped.
        var time = new FakeTimeProvider(Start);
        var primary = new WaitingClient();
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 6)),
            timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(primary.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.False(primary.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Equal(DecisionErrorKind.Timeout, result.Error.Kind);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task A_deadline_already_past_still_calls_step_zero_with_a_live_token()
    {
        var time = new FakeTimeProvider(Start);
        CancellationToken seenToken = default;
        var primary = new StubDecisionClient((_, token) =>
        {
            seenToken = token;
            return new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed")));
        });
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(primary, Options(null, Step("clef", clef, 0)), timeProvider: time);

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = Start.AddSeconds(-1) });

        Assert.Equal(1, primary.Calls);
        Assert.False(seenToken.IsCancellationRequested);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task The_last_step_is_cut_at_the_deadline()
    {
        var time = new FakeTimeProvider(Start);
        var clef = new WaitingClient();
        using var client = new FallbackDecisionClient(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3)),
            timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await clef.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.False(clef.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Equal(DecisionErrorKind.Timeout, result.Error.Kind);
        Assert.Equal(CutMessage, result.Error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cut_step_fails_as_Timeout_with_the_fixed_message(bool stepReturnsAFailure)
    {
        // Whether the cut step throws or turns the cancellation into a failure of its own, the stage's error stands.
        var time = new FakeTimeProvider(Start);
        var primary = new WaitingClient(failOnCancel: stepReturnsAFailure);
        var clef = StubDecisionClient.Answering(StepAnswers);
        DecisionError? seen = null;
        var options = Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3));
        options.ShouldFallBack = error =>
        {
            seen = error;
            return false;
        };
        using var client = new FallbackDecisionClient(primary, options, timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromMilliseconds(4700));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Same(seen, result.Error);
        Assert.Equal(DecisionErrorKind.Timeout, result.Error.Kind);
        Assert.Equal(CutMessage, result.Error.Message);
        Assert.Null(result.Error.StatusCode);
        Assert.Null(result.Error.RetryAfter);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task A_step_answering_after_the_cut_keeps_its_answer()
    {
        // The cut cancels the token; a step that answers anyway has answered.
        var time = new FakeTimeProvider(Start);
        var primary = new StubDecisionClient((request, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(4.8));
            return new(Result<DecisionResponse, DecisionError>.Success(StubDecisionClient.Respond(request.Definition, StepAnswers)));
        });
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(primary, Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3)), timeProvider: time);

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Path);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task The_callers_cancellation_throws_with_the_callers_token()
    {
        var time = new FakeTimeProvider(Start);
        var primary = new WaitingClient();
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var cts = new CancellationTokenSource();
        using var client = new FallbackDecisionClient(primary, Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3)), timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cts.Token, caught.CancellationToken);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task The_callers_cancellation_wins_when_the_cut_passes_too()
    {
        var time = new FakeTimeProvider(Start);
        using var cts = new CancellationTokenSource();
        var primary = new WaitingClient(onStart: () =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            cts.Cancel();
        });
        using var client = new FallbackDecisionClient(
            primary,
            Options(TimeSpan.FromSeconds(5), Step("clef", StubDecisionClient.Answering(StepAnswers), 0.3)),
            timeProvider: time);

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cts.Token, caught.CancellationToken);
    }

    [Fact]
    public async Task The_callers_cancellation_on_a_later_step_throws_with_the_callers_token()
    {
        var time = new FakeTimeProvider(Start);
        var clef = new WaitingClient();
        using var cts = new CancellationTokenSource();
        using var client = new FallbackDecisionClient(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            Options(TimeSpan.FromSeconds(5), Step("clef", clef, 0.3)),
            timeProvider: time);

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask();
        await clef.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cts.Token, caught.CancellationToken);
    }

    [Fact]
    public async Task A_step_failing_after_the_callers_cancellation_stops_the_chain()
    {
        // A step that turns the caller's cancellation into a failure result does not move the chain on.
        using var cts = new CancellationTokenSource();
        var primary = new StubDecisionClient((_, _) =>
        {
            cts.Cancel();
            return new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Timeout, "cancelled")));
        });
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(primary, Options(null, Step("clef", clef, 0)));

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask());

        Assert.Equal(cts.Token, caught.CancellationToken);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task The_callers_cancellation_without_a_deadline_throws()
    {
        var primary = new WaitingClient();
        using var cts = new CancellationTokenSource();
        using var client = new FallbackDecisionClient(primary, Options(null, Step("clef", StubDecisionClient.Answering(StepAnswers), 0)));

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cts.Token, caught.CancellationToken);
    }

    [Fact]
    public async Task A_deadline_beyond_the_timer_range_still_falls_back()
    {
        var time = new FakeTimeProvider(Start);
        var clef = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            Options(null, Step("clef", clef, 1)),
            timeProvider: time);

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State) { Deadline = DateTimeOffset.MaxValue });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, clef.Calls);
    }

    /// <summary>A client that waits until its token is cancelled, then throws, or returns a time-out failure of its own.</summary>
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
}
