using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>The fallback stage's four log events: their ids, levels and the values they carry.</summary>
public sealed class FallbackLoggingTests
{
    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, QuestionAnswer> Answers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.7, [0.2, 0.7, 0.1]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.85, [0.05, 0.85, 0.1]),
    };

    private static FallbackDecisionClient Stage(LogCapture logs, IDecisionClient primary, TimeProvider? time = null, TimeSpan? budget = null, params FallbackStep[] steps)
    {
        var options = new FallbackOptions { Budget = budget };
        foreach (var step in steps)
        {
            options.Steps.Add(step);
        }

        return new FallbackDecisionClient(primary, options, logs.Factory.CreateLogger("test"), time);
    }

    private static string Value(FakeLogRecord record, string key)
        => record.StructuredState!.Single(pair => string.Equals(pair.Key, key, StringComparison.Ordinal)).Value!;

    private static StubDecisionClient Failing(DecisionErrorKind kind = DecisionErrorKind.Server) => StubDecisionClient.Failing(kind);

    [Fact]
    public async Task Step_zero_answering_logs_nothing()
    {
        using var logs = new LogCapture();
        using var client = Stage(logs, StubDecisionClient.Answering(Answers), steps: new FallbackStep("clef", StubDecisionClient.Answering(Answers)));

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task A_step_zero_failure_the_chain_rejects_logs_nothing()
    {
        using var logs = new LogCapture();
        using var client = Stage(logs, Failing(DecisionErrorKind.Validation), steps: new FallbackStep("clef", StubDecisionClient.Answering(Answers)));

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task A_fallback_answer_logs_each_failed_step_and_the_answering_step()
    {
        using var logs = new LogCapture();
        using var client = Stage(
            logs,
            Failing(),
            steps: [new FallbackStep("clef", Failing(DecisionErrorKind.RateLimited)), new FallbackStep("hosted", StubDecisionClient.Answering(Answers))]);

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal([1017, 1017, 1020], logs.EventIds);
        var failed = logs.Records.Where(record => record.Id.Id == 1017).ToArray();
        Assert.All(failed, record => Assert.Equal(LogLevel.Warning, record.Level));
        Assert.Equal(["primary", "clef"], failed.Select(record => Value(record, "Step")));
        Assert.Equal(["Server", "RateLimited"], failed.Select(record => Value(record, "ErrorKind")));
        var fellBack = logs.Only(1020);
        Assert.Equal(LogLevel.Information, fellBack.Level);
        Assert.Equal(nameof(DecisionLog.FellBack), fellBack.Id.Name);
        Assert.Equal("hosted", Value(fellBack, "Step"));
        Assert.Equal("2", Value(fellBack, "StepsTried"));
    }

    [Fact]
    public async Task An_exhausted_chain_logs_the_steps_tried_and_the_last_error_kind()
    {
        using var logs = new LogCapture();
        using var client = Stage(logs, Failing(), steps: new FallbackStep("clef", Failing(DecisionErrorKind.Overloaded)));

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        // The last step's failure logs no 1017: no step is left to move on to, so 1019 alone says how it failed.
        Assert.Equal([1017, 1019], logs.EventIds);
        Assert.Equal("primary", Value(logs.Only(1017), "Step"));
        var exhausted = logs.Only(1019);
        Assert.Equal(LogLevel.Warning, exhausted.Level);
        Assert.Equal(nameof(DecisionLog.FallbackExhausted), exhausted.Id.Name);
        Assert.Equal("1", Value(exhausted, "StepsTried"));
        Assert.Equal("Overloaded", Value(exhausted, "ErrorKind"));
    }

    [Fact]
    public async Task An_exhausted_chain_of_three_logs_1017_for_each_step_but_the_last()
    {
        using var logs = new LogCapture();
        using var client = Stage(
            logs,
            Failing(),
            steps: [new FallbackStep("clef", Failing(DecisionErrorKind.RateLimited)), new FallbackStep("hosted", Failing(DecisionErrorKind.Overloaded))]);

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal([1017, 1017, 1019], logs.EventIds);
        Assert.Equal(["primary", "clef"], logs.Records.Where(record => record.Id.Id == 1017).Select(record => Value(record, "Step")));
        Assert.Equal("2", Value(logs.Only(1019), "StepsTried"));
        Assert.Equal("Overloaded", Value(logs.Only(1019), "ErrorKind"));
    }

    [Fact]
    public async Task A_later_failure_the_chain_rejects_logs_exhausted_with_its_kind()
    {
        using var logs = new LogCapture();
        using var client = Stage(
            logs,
            Failing(),
            steps: [new FallbackStep("clef", Failing(DecisionErrorKind.Refused)), new FallbackStep("hosted", StubDecisionClient.Answering(Answers))]);

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal([1017, 1019], logs.EventIds);
        Assert.Equal("primary", Value(logs.Only(1017), "Step"));
        Assert.Equal("1", Value(logs.Only(1019), "StepsTried"));
        Assert.Equal("Refused", Value(logs.Only(1019), "ErrorKind"));
    }

    [Fact]
    public async Task A_step_skipped_for_time_logs_its_name()
    {
        using var logs = new LogCapture();
        var time = new FakeTimeProvider(Start);
        var primary = new StubDecisionClient((_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(4));
            return new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed")));
        });
        using var client = Stage(
            logs,
            primary,
            time,
            TimeSpan.FromSeconds(5),
            new FallbackStep("clef", StubDecisionClient.Answering(Answers)) { ExpectedLatency = TimeSpan.FromSeconds(2) },
            new FallbackStep("hosted", StubDecisionClient.Answering(Answers)) { ExpectedLatency = TimeSpan.FromSeconds(0.5) });

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal([1017, 1018, 1020], logs.EventIds);
        var skipped = logs.Only(1018);
        Assert.Equal(LogLevel.Information, skipped.Level);
        Assert.Equal(nameof(DecisionLog.FallbackStepSkippedForTime), skipped.Id.Name);
        Assert.Equal("clef", Value(skipped, "Step"));
        Assert.Equal("2", Value(logs.Only(1020), "StepsTried"));
    }

    [Fact]
    public async Task Every_later_step_skipped_logs_exhausted_with_step_zeros_kind()
    {
        using var logs = new LogCapture();
        var time = new FakeTimeProvider(Start);
        var primary = new StubDecisionClient((_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Network, "failed")));
        });
        using var client = Stage(
            logs, primary, time, TimeSpan.FromSeconds(5), new FallbackStep("clef", StubDecisionClient.Answering(Answers)) { ExpectedLatency = TimeSpan.FromSeconds(6) });

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal([1017, 1018, 1019], logs.EventIds);
        Assert.Equal("1", Value(logs.Only(1019), "StepsTried"));
        Assert.Equal("Network", Value(logs.Only(1019), "ErrorKind"));
    }

    [Fact]
    public async Task A_cut_step_logs_DeadlineExceeded()
    {
        using var logs = new LogCapture();
        var time = new FakeTimeProvider(Start);
        var primary = new FallbackTimeTests.WaitingClient(failOnCancel: true);
        using var client = Stage(
            logs, primary, time, TimeSpan.FromSeconds(5), new FallbackStep("clef", StubDecisionClient.Answering(Answers)) { ExpectedLatency = TimeSpan.FromSeconds(0.3) });

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(4.7));
        await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([1017, 1020], logs.EventIds);
        Assert.Equal("primary", Value(logs.Only(1017), "Step"));
        Assert.Equal("DeadlineExceeded", Value(logs.Only(1017), "ErrorKind"));
        Assert.Equal(nameof(DecisionLog.FallbackStepFailed), logs.Only(1017).Id.Name);
    }

    [Fact]
    public async Task A_cut_last_step_logs_exhausted_with_DeadlineExceeded()
    {
        using var logs = new LogCapture();
        var time = new FakeTimeProvider(Start);
        var clef = new FallbackTimeTests.WaitingClient();
        using var client = Stage(logs, Failing(), time, TimeSpan.FromSeconds(5), new FallbackStep("clef", clef));

        var call = client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask();
        await clef.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(5));
        await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([1017, 1019], logs.EventIds);
        Assert.Equal("Server", Value(logs.Only(1017), "ErrorKind"));
        Assert.Equal("DeadlineExceeded", Value(logs.Only(1019), "ErrorKind"));
    }

    [Fact]
    public async Task A_step_answering_another_question_set_logs_DefinitionMismatch()
    {
        using var logs = new LogCapture();
        var clef = StubDecisionClient.Responding(request => StubDecisionClient.Respond(new QuestionSetDefinition([.. request.Definition.Questions]), Answers));
        using var client = Stage(logs, Failing(), steps: new FallbackStep("clef", clef));

        await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal([1017, 1019], logs.EventIds);
        Assert.Equal("Server", Value(logs.Only(1017), "ErrorKind"));
        Assert.Equal("DefinitionMismatch", Value(logs.Only(1019), "ErrorKind"));
    }

    [Fact]
    public async Task A_failure_after_the_callers_cancellation_logs_no_step_failure()
    {
        // Step 0 turns the caller's cancellation into a failure the chain would accept; the stage throws instead of moving on.
        using var logs = new LogCapture();
        using var cts = new CancellationTokenSource();
        var primary = new StubDecisionClient((_, _) =>
        {
            cts.Cancel();
            return new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Timeout, "cancelled")));
        });
        var clef = StubDecisionClient.Answering(Answers);
        using var client = Stage(logs, primary, steps: new FallbackStep("clef", clef));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask());

        Assert.Equal(0, clef.Calls);
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task Without_a_logger_the_chain_still_runs()
    {
        using var client = new FallbackDecisionClient(Failing(), new FallbackOptions { Steps = { new FallbackStep("clef", StubDecisionClient.Answering(Answers)) } });

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsSuccess);
    }
}
