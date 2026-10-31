using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

/// <summary>
/// Shadow mode in the recording stage: the candidate runs in the background, never changes the primary's result or makes
/// it wait, and its answers reach the decision's record through a second write.
/// </summary>
[Collection(RecordingListeners.Name)]
public sealed class ShadowModeTests
{
    private static IDecisionClient Pipeline(IDecisionClient primary, DecisionRecorder recorder, ShadowOptions shadow)
        => primary.AsBuilder().UseRecording(shadow, recorder).Build();

    // Calls the pipeline and checks the primary's own result came back at once, while the candidate is still held.
    private static async Task<Result<DecisionResponse, DecisionError>> EvaluateAtOnceAsync(
        IDecisionClient client, DecisionRequest request, ShadowCandidate candidate, DecisionResponse primaryAnswer, CancellationToken cancellationToken = default)
    {
        var pending = client.EvaluateAsync(request, cancellationToken);
        Assert.True(pending.IsCompleted);
        var result = await pending;
        Assert.True(result.IsSuccess);
        Assert.Same(primaryAnswer, result.Value);
        Assert.False(candidate.IsReleased);
        return result;
    }

    // Flushes until the sink holds a decision with a shadow part.
    private static async Task<DecisionRecord> WaitShadowedAsync(DecisionRecorder recorder, InMemoryDecisionRecordSink sink, int count = 1)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            DecisionRecord? first = null;
            var shadowed = 0;
            foreach (var decision in sink.Records)
            {
                if (decision.Record.Shadow is not null)
                {
                    first ??= decision.Record;
                    shadowed++;
                }
            }

            if (shadowed >= count)
            {
                return first!;
            }

            Assert.True(deadline.Elapsed < Bound, $"{shadowed} decisions were shadowed, not {count}.");
            await Task.Delay(5);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(deadline.Elapsed < Bound, "The condition did not hold in time.");
            await Task.Delay(5);
        }
    }

    private static ScriptedClient Primary(DecisionResponse answer)
        => new ScriptedClient(Metadata).Then(Result<DecisionResponse, DecisionError>.Success(answer));

    [Fact]
    public async Task SlowCandidate_PrimaryReturnsAtOnce()
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var answer = Answer();
            using var client = Pipeline(Primary(answer), recorder, new ShadowOptions(candidate, 10));

            await EvaluateAtOnceAsync(client, Request(), candidate, answer);
            await candidate.WaitStartedAsync();

            candidate.Release();
            await WaitShadowedAsync(recorder, sink);
        }
    }

    [Fact]
    public async Task FailingCandidate_ErrorResult_PrimaryUnchanged_AndErrorKindRecorded()
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var answer = Answer();
            using var client = Pipeline(Primary(answer), recorder, new ShadowOptions(candidate, 10));

            await EvaluateAtOnceAsync(client, Request(), candidate, answer);
            await candidate.WaitStartedAsync();
            candidate.Release(Refusal());

            var shadow = (await WaitShadowedAsync(recorder, sink)).Shadow!;
            Assert.Equal(DecisionErrorKind.Refused, shadow.ErrorKind);
            Assert.Equal(["urgent", "topic"], shadow.RefusedKeys);
            Assert.Empty(shadow.Answers);
            Assert.Null(shadow.ServedModel);
            Assert.Null(shadow.Usage);
            Assert.Equal("anthropic", shadow.Provider);
        }
    }

    public static TheoryData<string, DecisionErrorKind> Thrown => new()
    {
        { "invalid", DecisionErrorKind.Server },
        { "http", DecisionErrorKind.Network },
        { "foreign cancellation", DecisionErrorKind.Server },
    };

    [Theory]
    [MemberData(nameof(Thrown))]
    public async Task ThrowingCandidate_PrimaryUnchanged_AndServerRecorded(string thrown, DecisionErrorKind expected)
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var answer = Answer();
            using var client = Pipeline(Primary(answer), recorder, new ShadowOptions(candidate, 10));

            await EvaluateAtOnceAsync(client, Request(), candidate, answer);
            await candidate.WaitStartedAsync();
            candidate.Throw(thrown switch
            {
                "invalid" => new InvalidOperationException("The candidate broke."),
                "http" => new HttpRequestException("The connection was reset."),
                _ => new OperationCanceledException("Cancelled by something else."),
            });

            var record = await WaitShadowedAsync(recorder, sink);
            Assert.Equal(expected, record.Shadow!.ErrorKind);
            Assert.Empty(record.Shadow.Answers);
            Assert.Empty(record.Shadow.RefusedKeys);

            // The primary's own part is the first write's.
            Assert.Equal("model-served", record.ServedModel);
            Assert.Null(record.ErrorKind);
        }
    }

    [Fact]
    public async Task CandidateOverCap_PrimaryUnchanged_AndSkippedCapCounted()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var first = Answer();
            var second = Answer();
            var primary = new ScriptedClient(Metadata)
                .Then(Result<DecisionResponse, DecisionError>.Success(first))
                .Then(Result<DecisionResponse, DecisionError>.Success(second));
            using var client = Pipeline(primary, recorder, new ShadowOptions(candidate, 1));

            await EvaluateAtOnceAsync(client, Request(), candidate, first);
            await candidate.WaitStartedAsync();
            await EvaluateAtOnceAsync(client, Request(), candidate, second);

            Assert.Equal(1, metrics.Sum("minos.recording.shadows", "skipped_cap"));
            candidate.Release();
            await WaitShadowedAsync(recorder, sink);
            Assert.Single(candidate.Calls);
        }

        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "written"));
        Assert.Equal(2, metrics.Sum("minos.recording.records", "written"));
    }

    [Fact]
    public async Task CandidateBusy_PrimaryUnchanged_AndSkippedBusyCounted()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var first = Answer();
            var second = Answer();
            var primary = new ScriptedClient(Metadata)
                .Then(Result<DecisionResponse, DecisionError>.Success(first))
                .Then(Result<DecisionResponse, DecisionError>.Success(second));
            using var client = Pipeline(primary, recorder, new ShadowOptions(candidate, 10) { MaxConcurrent = 1 });

            await EvaluateAtOnceAsync(client, Request(), candidate, first);
            await candidate.WaitStartedAsync();
            await EvaluateAtOnceAsync(client, Request(), candidate, second);

            Assert.Equal(1, metrics.Sum("minos.recording.shadows", "skipped_busy"));
            candidate.Release();
            await WaitShadowedAsync(recorder, sink);
            Assert.Single(candidate.Calls);
        }
    }

    [Fact]
    public async Task CallerCancellation_DoesNotCancelTheCandidate()
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var answer = Answer();
            using var client = Pipeline(Primary(answer), recorder, new ShadowOptions(candidate, 10));
            using var caller = new CancellationTokenSource();

            await EvaluateAtOnceAsync(client, Request(), candidate, answer, caller.Token);
            await candidate.WaitStartedAsync();
            await caller.CancelAsync();

            var call = Assert.Single(candidate.Calls);
            Assert.False(call.Token.IsCancellationRequested);
            candidate.Release();
            var shadow = (await WaitShadowedAsync(recorder, sink)).Shadow!;
            Assert.Null(shadow.ErrorKind);
        }
    }

    [Fact]
    public async Task CandidateTimeout_RecordsTimeout()
    {
        var time = new FakeTimeProvider(TestRecords.T0);
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink, time: time);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var answer = Answer();
            var options = new ShadowOptions(candidate, 10) { Timeout = TimeSpan.FromSeconds(3) };
            using var client = Pipeline(Primary(answer), recorder, options);

            await EvaluateAtOnceAsync(client, Request(), candidate, answer);
            await candidate.WaitStartedAsync();
            time.Advance(TimeSpan.FromSeconds(3) + TimeSpan.FromMilliseconds(1));

            var shadow = (await WaitShadowedAsync(recorder, sink)).Shadow!;
            Assert.Equal(DecisionErrorKind.Timeout, shadow.ErrorKind);
            Assert.True(Assert.Single(candidate.Calls).Token.IsCancellationRequested);
        }
    }

    [Fact]
    public async Task Shadowed_RecordHoldsCandidateAnswers()
    {
        using var metrics = new MetricCapture();
        var time = new FakeTimeProvider(TestRecords.T0);
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink, time: time);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            var answer = Answer();
            using var client = Pipeline(Primary(answer), recorder, new ShadowOptions(candidate, 10) { Model = "candidate-requested" });

            using (DecisionRecording.Begin("d-shadow", "customer-7"))
            {
                await EvaluateAtOnceAsync(client, Request(), candidate, answer);
            }

            await candidate.WaitStartedAsync();
            time.Advance(TimeSpan.FromMilliseconds(40));
            candidate.Release();

            var record = await WaitShadowedAsync(recorder, sink);
            Assert.Equal("d-shadow", record.DecisionId);
            Assert.Equal("customer-7", record.ErasureKey);
            var shadow = record.Shadow!;
            Assert.Equal("anthropic", shadow.Provider);
            Assert.Equal("https://candidate.example.test/", shadow.Endpoint);
            Assert.Equal("candidate-default", shadow.DefaultModel);
            Assert.Equal("candidate-requested", shadow.RequestedModel);
            Assert.Equal("candidate-served", shadow.ServedModel);
            Assert.Equal("resp-shadow", shadow.ResponseId);
            Assert.Equal("Bedrock", shadow.UpstreamProvider);
            Assert.Equal(TimeSpan.FromMilliseconds(40), shadow.Latency);
            Assert.Equal(130, shadow.Usage!.InputTokens);
            Assert.Null(shadow.ErrorKind);
            Assert.Empty(shadow.RefusedKeys);
            Assert.Collection(
                shadow.Answers,
                urgent => Assert.Equal(("urgent", QuestionKind.Noul, 0.2), (urgent.Key, urgent.Kind, urgent.Probability)),
                topic => Assert.Equal(("topic", QuestionKind.Choice, 0), (topic.Key, topic.Kind, topic.ChosenIndex)));

            // The primary's part is unchanged by the second write.
            Assert.Equal("openai", record.Provider);
            Assert.Equal("model-served", record.ServedModel);
            Assert.Equal(2, record.Answers.Count);
            Assert.Single(sink.Records);
        }

        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "written"));
        Assert.Equal(1, metrics.Sum("minos.recording.records"));
    }

    [Fact]
    public async Task Candidate_GetsUnredactedState_AndOptionsModel()
    {
        var sink = new InMemoryDecisionRecordSink();
        var options = new RecordingOptions { Redact = _ => RedactionResult.Keep("[masked]") };
        var (recorder, _, factory) = Recorder(sink, options);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate(held: false);
            using var client = Pipeline(new ScriptedClient(Metadata), recorder, new ShadowOptions(candidate, 10) { Model = "candidate-requested" });
            var image = DecisionImage.FromBytes(new byte[] { 1, 2, 3 }, "image/png");
            var request = Request("My card has not arrived.") with { Model = "primary-model", Images = [image] };

            Assert.True((await client.EvaluateAsync(request)).IsSuccess);

            var record = await WaitShadowedAsync(recorder, sink);
            Assert.Equal("[masked]", record.State.ToString());
            Assert.Equal("primary-model", record.RequestedModel);
            Assert.Equal("candidate-requested", record.Shadow!.RequestedModel);

            var seen = Assert.Single(candidate.Calls).Request;
            Assert.Equal("My card has not arrived.", seen.State.ToString());
            Assert.Equal("candidate-requested", seen.Model);
            Assert.Same(request.Definition, seen.Definition);
            Assert.Equal(0, seen.RetryAttempt);
            Assert.Equal(request.Images, seen.Images);
        }
    }

    public static TheoryData<string> NotShadowed => new() { "failed", "cached", "retried", "redaction dropped", "queue dropped" };

    [Theory]
    [MemberData(nameof(NotShadowed))]
    public async Task NotShadowed_FailedCachedRetriedRedactionDroppedOrQueueDropped(string scenario)
    {
        using var metrics = new MetricCapture();
        var queueDropped = string.Equals(scenario, "queue dropped", StringComparison.Ordinal);
        var blocking = new BlockingSink();
        var options = new RecordingOptions
        {
            Capacity = queueDropped ? 1 : 1024,
            Redact = state => state.ToString().Contains("drop me", StringComparison.Ordinal) ? RedactionResult.Drop : RedactionResult.Keep(state),
        };
        var (recorder, _, factory) = Recorder(blocking, options);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate(held: false);
            var primary = new ScriptedClient(Metadata);
            using var client = Pipeline(primary, recorder, new ShadowOptions(candidate, 1) { MaxConcurrent = 1 });
            var request = Request();
            switch (scenario)
            {
                case "failed":
                    primary.Then(Error());
                    break;
                case "cached":
                    primary.Then(Result<DecisionResponse, DecisionError>.Success(Answer(isCached: true)));
                    break;
                case "retried":
                    request = request with { RetryAttempt = 1 };
                    break;
                case "redaction dropped":
                    request = Request("drop me");
                    break;
                default:
                {
                    // The sink holds one record and the queue a second, through a pipeline without a shadow.
                    using var plain = new ScriptedClient(Metadata).AsBuilder().UseRecording(recorder).Build();
                    await plain.EvaluateAsync(Request());
                    await blocking.WaitEnteredAsync();
                    await plain.EvaluateAsync(Request());
                    break;
                }
            }

            if (!queueDropped)
            {
                blocking.Release();
            }

            await client.EvaluateAsync(request);
            blocking.Release();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            Assert.Empty(candidate.Calls);
            Assert.Equal(0, metrics.Sum("minos.recording.shadows"));

            // Both slots are still free: a cap of 1 and one running candidate let the next decision's candidate start.
            await client.EvaluateAsync(Request());
            await candidate.WaitStartedAsync();
            Assert.Single(candidate.Calls);
        }
    }

    [Fact]
    public async Task Cap_ResetsAtUtcMidnight()
    {
        using var metrics = new MetricCapture();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 23, 59, 0, TimeSpan.Zero));
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink, time: time);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate(held: false);
            using var client = Pipeline(new ScriptedClient(Metadata), recorder, new ShadowOptions(candidate, 1));

            await client.EvaluateAsync(Request());
            await WaitShadowedAsync(recorder, sink);
            await client.EvaluateAsync(Request());
            Assert.Equal(1, metrics.Sum("minos.recording.shadows", "skipped_cap"));

            time.SetUtcNow(new DateTimeOffset(2026, 10, 23, 0, 0, 1, TimeSpan.Zero));
            await client.EvaluateAsync(Request());
            await WaitShadowedAsync(recorder, sink, count: 2);

            Assert.Equal(2, candidate.Calls.Count);
            Assert.Equal(1, metrics.Sum("minos.recording.shadows", "skipped_cap"));
        }
    }

    [Fact]
    public async Task Cap_UnderConcurrency_NeverStartsMoreThanTheCap()
    {
        const int Decisions = 64;
        const int Cap = 10;
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            using var client = Pipeline(
                new ScriptedClient(Metadata, during: async _ => await Task.Yield()), recorder, new ShadowOptions(candidate, Cap) { MaxConcurrent = Decisions });

            await Task.WhenAll(Enumerable.Range(0, Decisions).Select(_ => Task.Run(async () =>
                Assert.True((await client.EvaluateAsync(Request())).IsSuccess))));

            Assert.Equal(Decisions - Cap, metrics.Sum("minos.recording.shadows", "skipped_cap"));
            await candidate.WaitStartedAsync(Cap);
            candidate.Release();
            await WaitShadowedAsync(recorder, sink, Cap);

            Assert.Equal(Cap, candidate.Calls.Count);
            Assert.Equal(0, metrics.Sum("minos.recording.shadows", "skipped_busy"));
        }

        Assert.Equal(Cap, metrics.Sum("minos.recording.shadows", "written"));
    }

    [Fact]
    public async Task BusySkip_GivesItsCapSlotBack()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate();
            using var client = Pipeline(new ScriptedClient(Metadata), recorder, new ShadowOptions(candidate, 2) { MaxConcurrent = 1 });

            await client.EvaluateAsync(Request());
            await candidate.WaitStartedAsync();
            await client.EvaluateAsync(Request());
            Assert.Equal(1, metrics.Sum("minos.recording.shadows", "skipped_busy"));

            candidate.Release();
            await WaitShadowedAsync(recorder, sink);
            await client.EvaluateAsync(Request());
            await WaitShadowedAsync(recorder, sink, count: 2);

            Assert.Equal(2, candidate.Calls.Count);
            Assert.Equal(0, metrics.Sum("minos.recording.shadows", "skipped_cap"));
        }
    }

    [Fact]
    public async Task Candidate_RunsWithoutTheCallersScopeOrActivity()
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate(held: false);
            using var client = Pipeline(new ScriptedClient(Metadata), recorder, new ShadowOptions(candidate, 10));

            using (var activity = new Activity("caller").Start())
            using (DecisionRecording.Begin("d-1"))
            {
                Assert.NotNull(Activity.Current);
                Assert.NotNull(DecisionRecording.Current);
                await client.EvaluateAsync(Request());
            }

            await WaitShadowedAsync(recorder, sink);
            var call = Assert.Single(candidate.Calls);
            Assert.Null(call.Activity);
            Assert.Null(call.Scope);
        }
    }

    [Fact]
    public async Task Candidate_StartsUnderSuppressedFlow()
    {
        // A caller that already suppressed the flow: the stage must not suppress it a second time, which throws.
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var candidate = new ShadowCandidate(held: false);
            using var client = Pipeline(new ScriptedClient(Metadata), recorder, new ShadowOptions(candidate, 10));

            ValueTask<Result<DecisionResponse, DecisionError>> pending;
            using (ExecutionContext.SuppressFlow())
            {
                pending = client.EvaluateAsync(Request());
            }

            Assert.True((await pending).IsSuccess);

            await WaitShadowedAsync(recorder, sink);
            Assert.Single(candidate.Calls);
        }
    }

    [Fact]
    public async Task Dispose_WhileCandidateRuns_DropsItsWrite()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        {
            var candidate = new ShadowCandidate();
            using var client = Pipeline(new ScriptedClient(Metadata), recorder, new ShadowOptions(candidate, 10));

            await client.EvaluateAsync(Request());
            await candidate.WaitStartedAsync();
            await recorder.DisposeAsync().AsTask().WaitAsync(Bound);

            Assert.True(Assert.Single(candidate.Calls).Token.IsCancellationRequested);
            await WaitUntilAsync(() => metrics.Sum("minos.recording.shadows", "dropped") == 1);
        }

        var decision = Assert.Single(sink.Records);
        Assert.Null(decision.Record.Shadow);
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "written"));
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "failed"));
    }

    [Fact]
    public async Task ShadowOptions_RejectsBadArguments()
    {
        var candidate = new ShadowCandidate();
        Assert.Throws<ArgumentNullException>(() => new ShadowOptions(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowOptions(candidate, 0));

        var options = new ShadowOptions(candidate, int.MaxValue);
        Assert.Same(candidate, options.Candidate);
        Assert.Equal(int.MaxValue, options.DailyCap);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal(4, options.MaxConcurrent);
        Assert.Null(options.Model);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Timeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Timeout = TimeSpan.FromSeconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Timeout = Timeout.InfiniteTimeSpan);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Timeout = TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromMilliseconds(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxConcurrent = 0);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal(4, options.MaxConcurrent);

        var (recorder, _, factory) = Recorder(new InMemoryDecisionRecordSink());
        using (factory)
        await using (recorder)
        {
            using var recording = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            var thrown = Assert.Throws<ArgumentException>(() => new ShadowOptions(recording, 1));
            Assert.Equal("candidate", thrown.ParamName);
            Assert.StartsWith("The candidate records its own decisions: shadow a client without UseRecording.", thrown.Message, StringComparison.Ordinal);

            Assert.Throws<ArgumentNullException>(() => RecordingDecisionClientBuilderExtensions.UseRecording(null!, options));
            Assert.Throws<ArgumentNullException>(() => new ScriptedClient().AsBuilder().UseRecording((ShadowOptions)null!, recorder));
            Assert.Throws<ArgumentNullException>(() => new RecordingDecisionClient(new ScriptedClient(), null!, options));
        }
    }
}
