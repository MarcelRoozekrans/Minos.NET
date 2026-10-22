using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using static Minos.Recording.Tests.TestRecords;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class DecisionRecorderTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static (DecisionRecorder Recorder, FakeLogCollector Logs, ILoggerFactory Factory) Create(
        IDecisionRecordSink sink, RecordingOptions? options = null, TimeProvider? time = null)
    {
        var logs = new FakeLogCollector();
        var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new FakeLoggerProvider(logs)));
        return (new DecisionRecorder(sink, options, factory, time), logs, factory);
    }

    private static string? Field(FakeLogRecord record, string key)
        => record.StructuredState!.Single(pair => string.Equals(pair.Key, key, StringComparison.Ordinal)).Value;

    [Fact]
    public async Task Items_AreHandedToTheSink_InQueueOrder()
    {
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            Assert.True(recorder.TryEnqueue(Record("d-1")));
            await recorder.AddOutcomeAsync("d-1", Signal());
            await recorder.EraseAsync("customer-7").AsTask().WaitAsync(Bound);
            await recorder.EraseDecisionAsync("d-2").AsTask().WaitAsync(Bound);
        }

        Assert.Equal(["write:d-1", "outcome:d-1", "erase:customer-7", "erase-decision:d-2"], sink.Calls);
        Assert.False(sink.Overlapped);
    }

    [Fact]
    public async Task AFullQueue_DropsRecords_AndCountsThem()
    {
        using var metrics = new MetricCapture();
        var sink = new BlockingSink();
        var (recorder, logs, factory) = Create(sink, new RecordingOptions { Capacity = 2 });
        using (factory)
        await using (recorder)
        {
            Assert.True(recorder.TryEnqueue(Record("d-1")));
            await sink.WaitEnteredAsync();
            Assert.True(recorder.TryEnqueue(Record("d-2")));
            Assert.True(recorder.TryEnqueue(Record("d-3")));

            Assert.False(recorder.TryEnqueue(Record("d-4")));

            Assert.Equal(1, metrics.Sum("minos.recording.records", "dropped_full"));
            sink.Release();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(["write:d-1", "write:d-2", "write:d-3"], sink.Spy.Calls);
        Assert.Equal(3, metrics.Sum("minos.recording.records", "written"));
        var dropped = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1009);
        Assert.Equal(LogLevel.Warning, dropped.Level);
        Assert.Equal("1", Field(dropped, "Count"));
    }

    [Fact]
    public async Task AFullQueue_MakesAnErasureWait_NotDrop()
    {
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink, new RecordingOptions { Capacity = 2 });
        using (factory)
        await using (recorder)
        {
            recorder.TryEnqueue(Record("d-1"));
            await sink.WaitEnteredAsync();
            recorder.TryEnqueue(Record("d-2"));
            recorder.TryEnqueue(Record("d-3"));

            var erase = recorder.EraseAsync("customer-7").AsTask();
            var outcome = recorder.AddOutcomeAsync("d-1", Signal()).AsTask();
            Assert.False(erase.IsCompleted);
            Assert.False(outcome.IsCompleted);

            sink.Release();
            await erase.WaitAsync(Bound);
            await outcome.WaitAsync(Bound);
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(["write:d-1", "write:d-2", "write:d-3", "erase:customer-7", "outcome:d-1"], sink.Spy.Calls);
    }

    [Fact]
    public async Task AnErasureWaitingForSpace_HonoursItsToken()
    {
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink, new RecordingOptions { Capacity = 1 });
        using (factory)
        await using (recorder)
        {
            recorder.TryEnqueue(Record("d-1"));
            await sink.WaitEnteredAsync();
            recorder.TryEnqueue(Record("d-2"));
            using var cancel = new CancellationTokenSource();

            var erase = recorder.EraseAsync("customer-7", cancel.Token).AsTask();
            await cancel.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => erase.WaitAsync(Bound));
            sink.Release();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            Assert.Equal(["write:d-1", "write:d-2"], sink.Spy.Calls);
        }
    }

    [Fact]
    public async Task Erase_CompletesAfterTheSinkErased()
    {
        using var metrics = new MetricCapture();
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var erase = recorder.EraseAsync("customer-7").AsTask();
            await sink.WaitEnteredAsync();
            Assert.False(erase.IsCompleted);

            sink.Release();
            await erase.WaitAsync(Bound);

            Assert.Equal(["erase:customer-7"], sink.Spy.Calls);
            var byId = recorder.EraseDecisionAsync("d-9").AsTask();
            await byId.WaitAsync(Bound);
            Assert.Equal(["erase:customer-7", "erase-decision:d-9"], sink.Spy.Calls);
        }

        Assert.Equal(2, metrics.Sum("minos.recording.erasures", "written"));
    }

    [Fact]
    public async Task Erase_Fails_WithRecordingException_WhenTheSinkThrows()
    {
        using var metrics = new MetricCapture();
        var sink = new ThrowingSink(call => call.StartsWith("erase", StringComparison.Ordinal));
        var (recorder, logs, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var byKey = await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseAsync("KEY-SENTINEL").AsTask().WaitAsync(Bound));
            var byId = await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseDecisionAsync("ID-SENTINEL").AsTask().WaitAsync(Bound));

            foreach (var exception in new[] { byKey, byId })
            {
                Assert.DoesNotContain("SENTINEL", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(ThrowingSink.MessagePrefix.Trim(), exception.Message, StringComparison.Ordinal);
            }

            Assert.Same(sink.Thrown[0], byKey.InnerException);
            Assert.Same(sink.Thrown[1], byId.InnerException);
        }

        Assert.Equal(2, metrics.Sum("minos.recording.erasures", "failed"));
        var failures = logs.GetSnapshot().Where(record => record.Id.Id == 1010).ToArray();
        Assert.Equal(["erase", "erase-decision"], failures.Select(record => Field(record, "Operation")));
        Assert.All(failures, record =>
        {
            Assert.Equal("InvalidOperationException", Field(record, "ExceptionType"));
            Assert.Null(record.Exception);
            Assert.DoesNotContain("SENTINEL", record.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AddOutcome_CompletesWhenQueued()
    {
        using var metrics = new MetricCapture();
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            recorder.TryEnqueue(Record("d-1"));
            await sink.WaitEnteredAsync();

            await recorder.AddOutcomeAsync("d-1", Confirmed()).AsTask().WaitAsync(Bound);

            Assert.Empty(sink.Spy.Calls);
            sink.Release();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            Assert.Equal(["write:d-1", "outcome:d-1"], sink.Spy.Calls);
        }

        Assert.Equal(1, metrics.Sum("minos.recording.outcomes", "written"));
    }

    [Fact]
    public async Task Flush_CompletesAfterEarlierItemsReachTheSink()
    {
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            recorder.TryEnqueue(Record("d-1"));
            recorder.TryEnqueue(Record("d-2"));
            await sink.WaitEnteredAsync();

            var flush = recorder.FlushAsync().AsTask();
            Assert.False(flush.IsCompleted);

            sink.Release();
            await flush.WaitAsync(Bound);
            Assert.Equal(["write:d-1", "write:d-2"], sink.Spy.Calls);
        }
    }

    [Fact]
    public async Task AThrowingSink_IsCountedAndSkipped_AndTheWriterGoesOn()
    {
        using var metrics = new MetricCapture();
        var sink = new ThrowingSink(call => call is "write:ID-SENTINEL" or "outcome:ID-SENTINEL");
        var (recorder, logs, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            recorder.TryEnqueue(Record("ID-SENTINEL"));
            await recorder.AddOutcomeAsync("ID-SENTINEL", Signal());
            recorder.TryEnqueue(Record("d-2"));
            await recorder.AddOutcomeAsync("d-2", Signal());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(["write:d-2", "outcome:d-2"], sink.Spy.Calls);
        Assert.Equal(1, metrics.Sum("minos.recording.records", "sink_failed"));
        Assert.Equal(1, metrics.Sum("minos.recording.records", "written"));
        Assert.Equal(1, metrics.Sum("minos.recording.outcomes", "failed"));
        Assert.Equal(1, metrics.Sum("minos.recording.outcomes", "written"));
        var failures = logs.GetSnapshot().Where(record => record.Id.Id == 1010).ToArray();
        Assert.Equal(["write", "add-outcome"], failures.Select(record => Field(record, "Operation")));
        Assert.All(failures, record =>
        {
            Assert.Equal(LogLevel.Warning, record.Level);
            Assert.DoesNotContain("SENTINEL", record.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Dispose_DrainsWithinTheTimeout()
    {
        var sink = new RecordingSpySink();
        var (recorder, logs, factory) = Create(sink);
        using (factory)
        {
            for (var i = 0; i < 50; i++)
            {
                Assert.True(recorder.TryEnqueue(Record($"d-{i}")));
            }

            await recorder.AddOutcomeAsync("d-49", Signal());
            await recorder.DisposeAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(51, sink.Calls.Count);
        Assert.Equal(50, sink.Inner.Records.Count);
        Assert.DoesNotContain(logs.GetSnapshot(), record => record.Id.Id == 1012);
    }

    [Fact]
    public async Task Dispose_ReturnsOnTime_WithASlowSink_AndCountsLostErasures()
    {
        using var metrics = new MetricCapture();
        var time = new FakeTimeProvider();
        var sink = new BlockingSink(honoursCancellation: false);
        var (recorder, logs, factory) = Create(sink, new RecordingOptions { FlushTimeout = TimeSpan.FromMilliseconds(100) }, time);
        try
        {
            using (factory)
            {
                recorder.TryEnqueue(Record("d-1"));
                await sink.WaitEnteredAsync();
                recorder.TryEnqueue(Record("d-2"));
                await recorder.AddOutcomeAsync("d-1", Signal());
                var byKey = recorder.EraseAsync("KEY-SENTINEL").AsTask();
                var byId = recorder.EraseDecisionAsync("ID-SENTINEL").AsTask();
                var flush = recorder.FlushAsync().AsTask();

                // The dispose waits on the recorder's clock: it is still waiting until the timeout passes there.
                var dispose = recorder.DisposeAsync().AsTask();
                time.Advance(TimeSpan.FromMilliseconds(99));
                Assert.False(dispose.IsCompleted);
                time.Advance(TimeSpan.FromMilliseconds(1));
                await dispose.WaitAsync(Bound);

                foreach (var lost in new[] { byKey, byId, flush })
                {
                    var exception = await Assert.ThrowsAsync<RecordingException>(() => lost.WaitAsync(Bound));
                    Assert.DoesNotContain("SENTINEL", exception.Message, StringComparison.Ordinal);
                }

                Assert.Equal(0, metrics.QueueLength());
            }

            AssertTimedOut(logs, records: 2, outcomes: 1, erasures: 2, flushes: 1);
            Assert.Equal(2, metrics.Sum("minos.recording.records", "dropped_flush_timeout"));
            Assert.Equal(0, metrics.Sum("minos.recording.records", "written"));

            // The abandoned write finishes late. Its record was already counted as lost, so it is not counted again.
            sink.Release();
            await WaitForAsync(() => sink.Spy.Calls.Contains("write:d-1"));
            await recorder.Writer.WaitAsync(Bound);
            Assert.Equal(0, metrics.Sum("minos.recording.records", "written"));
            Assert.Equal(2, metrics.Sum("minos.recording.records", "dropped_flush_timeout"));
        }
        finally
        {
            // Wait for the abandoned write, so it cannot count into the next test.
            sink.Release();
            await recorder.Writer.WaitAsync(Bound);
        }
    }

    [Fact]
    public async Task Dispose_CancelsASinkThatHonoursIt_AndCountsWhatWasLost()
    {
        using var metrics = new MetricCapture();
        var time = new FakeTimeProvider();
        var sink = new BlockingSink(honoursCancellation: true);
        var (recorder, logs, factory) = Create(sink, new RecordingOptions { FlushTimeout = TimeSpan.FromSeconds(1) }, time);
        using (factory)
        {
            var erase = recorder.EraseAsync("KEY-SENTINEL").AsTask();
            await sink.WaitEnteredAsync();
            recorder.TryEnqueue(Record("d-1"));

            var dispose = recorder.DisposeAsync().AsTask();
            time.Advance(TimeSpan.FromSeconds(1));
            await dispose.WaitAsync(Bound);

            var exception = await Assert.ThrowsAsync<RecordingException>(() => erase.WaitAsync(Bound));
            Assert.Equal(RecordingException.LostMessage, exception.Message);
            Assert.Empty(sink.Spy.Calls);
        }

        AssertTimedOut(logs, records: 1, outcomes: 0, erasures: 1, flushes: 0);
        Assert.Equal(1, metrics.Sum("minos.recording.records", "dropped_flush_timeout"));
        Assert.Equal(0, metrics.Sum("minos.recording.erasures", "failed"));
        Assert.DoesNotContain(logs.GetSnapshot(), record => record.Id.Id == 1010);
    }

    [Fact]
    public async Task Dispose_SettlesWhatWasLost_WhenTheSinksCancellationCallbackThrows()
    {
        using var metrics = new MetricCapture();
        var time = new FakeTimeProvider();
        var sink = new ThrowingCancellationSink();
        var (recorder, logs, factory) = Create(sink, new RecordingOptions { FlushTimeout = TimeSpan.FromSeconds(1) }, time);
        try
        {
            using (factory)
            {
                var first = recorder.EraseAsync("a").AsTask();
                await sink.Entered.Task.WaitAsync(Bound);
                var second = recorder.EraseAsync("b").AsTask();
                recorder.TryEnqueue(Record("d-1"));

                var dispose = recorder.DisposeAsync().AsTask();
                time.Advance(TimeSpan.FromSeconds(1));
                await dispose.WaitAsync(Bound);

                await Assert.ThrowsAsync<RecordingException>(() => first.WaitAsync(Bound));
                await Assert.ThrowsAsync<RecordingException>(() => second.WaitAsync(Bound));
                Assert.Equal(0, metrics.QueueLength());
            }

            AssertTimedOut(logs, records: 1, outcomes: 0, erasures: 2, flushes: 0);
            Assert.Equal(1, metrics.Sum("minos.recording.records", "dropped_flush_timeout"));

            // The failed cancellation is a sink call that failed, logged with its exception's type name only.
            var failed = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1010);
            Assert.Equal("cancel", Field(failed, "Operation"));
            Assert.Equal(nameof(InvalidOperationException), Field(failed, "ExceptionType"));
            Assert.DoesNotContain("SENTINEL", failed.Message, StringComparison.Ordinal);
        }
        finally
        {
            sink.Gate.TrySetResult();
            await recorder.Writer.WaitAsync(Bound);
        }
    }

    // An erasure whose token registration throws when it is cancelled, as a driver's cancel can, and that ignores the
    // cancellation until its gate opens.
    private sealed class ThrowingCancellationSink : IDecisionRecordSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(static () => throw new InvalidOperationException("SENTINEL driver cancel failed")))
            {
                Entered.TrySetResult();
                await Gate.Task;
            }
        }

        public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Calls_AfterDispose_Throw_AndTryEnqueueCountsTheRecord()
    {
        using var metrics = new MetricCapture();
        var (recorder, _, factory) = Create(new RecordingSpySink());
        using (factory)
        {
            await recorder.DisposeAsync();
            await recorder.DisposeAsync();

            Assert.False(recorder.TryEnqueue(Record()));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => recorder.AddOutcomeAsync("d-1", Signal()).AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => recorder.EraseAsync("customer-7").AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => recorder.EraseDecisionAsync("d-1").AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => recorder.FlushAsync().AsTask());
        }

        Assert.Equal(1, metrics.Sum("minos.recording.records", "dropped_disposed"));
        Assert.Equal(0, metrics.Sum("minos.recording.records", "dropped_full"));
    }

    [Fact]
    public async Task AThrowingLogger_NeitherStopsTheWriter_NorFailsTheDispose()
    {
        var sink = new ThrowingSink(call => call.StartsWith("erase:", StringComparison.Ordinal));
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new ThrowingLoggerProvider()));
        var recorder = new DecisionRecorder(sink, new RecordingOptions { Capacity = 1 }, factory);

        // A failing sink call logs 1010, which throws: the erasure still fails as it should, and the writer goes on.
        await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseAsync("customer-7").AsTask().WaitAsync(Bound));
        await recorder.EraseDecisionAsync("d-1").AsTask().WaitAsync(Bound);

        // A drop logs 1009 from the writer, which throws too.
        var blocking = new BlockingSink();
        var second = new DecisionRecorder(blocking, new RecordingOptions { Capacity = 1, FlushTimeout = TimeSpan.FromMilliseconds(50) }, factory);
        try
        {
            second.TryEnqueue(Record("d-1"));
            await blocking.WaitEnteredAsync();
            second.TryEnqueue(Record("d-2"));
            Assert.False(second.TryEnqueue(Record("d-3")));
            blocking.Release();
            await second.FlushAsync().AsTask().WaitAsync(Bound);
            await second.EraseAsync("customer-7").AsTask().WaitAsync(Bound);
            Assert.Equal(["write:d-1", "write:d-2", "erase:customer-7"], blocking.Spy.Calls);
        }
        finally
        {
            blocking.Release();
            await second.DisposeAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(["erase-decision:d-1"], sink.Spy.Calls);
        await recorder.DisposeAsync().AsTask().WaitAsync(Bound);
    }

    [Fact]
    public async Task AThrowingLogger_IsCounted()
    {
        using var metrics = new MetricCapture();
        var logFailuresBefore = metrics.Observe("minos.recording.log_failures");
        var sink = new ThrowingSink(call => call.StartsWith("write:", StringComparison.Ordinal));
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new ThrowingLoggerProvider()));
        await using (var recorder = new DecisionRecorder(sink, loggerFactory: factory))
        {
            recorder.TryEnqueue(Record("d-1"));
            recorder.TryEnqueue(Record("d-2"));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(2, metrics.Observe("minos.recording.log_failures") - logFailuresBefore);
        Assert.Equal(2, metrics.Sum("minos.recording.records", "sink_failed"));
    }

    [Fact]
    public async Task AThrowingMeterListener_DoesNotStopTheWriter()
    {
        var sink = new ThrowingSink(call => call.StartsWith("erase:", StringComparison.Ordinal));
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name.StartsWith("minos.recording.", StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Value is "failed" or "sink_failed")
                {
                    throw new InvalidOperationException("The listener is broken.");
                }
            }
        });
        listener.Start();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            // Counting the failure throws: the caller is still told, at once, and the writer goes on.
            await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseAsync("customer-7").AsTask().WaitAsync(Bound));
            await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseAsync("customer-8").AsTask().WaitAsync(Bound));
            await recorder.EraseDecisionAsync("d-1").AsTask().WaitAsync(Bound);
            Assert.True(recorder.TryEnqueue(Record("d-2")));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(["erase-decision:d-1", "write:d-2"], sink.Spy.Calls);
    }

    [Fact]
    public async Task AThrowingLogger_DoesNotFailADisposeThatTimesOut()
    {
        var sink = new BlockingSink();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new ThrowingLoggerProvider()));
        var time = new FakeTimeProvider();
        var recorder = new DecisionRecorder(sink, new RecordingOptions { FlushTimeout = TimeSpan.FromSeconds(1) }, factory, time);
        recorder.TryEnqueue(Record("d-1"));
        await sink.WaitEnteredAsync();
        var erase = recorder.EraseAsync("customer-7").AsTask();

        var dispose = recorder.DisposeAsync().AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        await dispose.WaitAsync(Bound);

        await Assert.ThrowsAsync<RecordingException>(() => erase.WaitAsync(Bound));
    }

    [Fact]
    public async Task AnErasureFailure_TheCallerStoppedWaitingFor_IsNotLeftUnobserved()
    {
        var unobserved = new List<AggregateException>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            lock (unobserved)
            {
                unobserved.Add(e.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await AbandonAFailingErasureAsync();
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            lock (unobserved)
            {
                Assert.DoesNotContain(unobserved, e => e.InnerExceptions.Any(inner => inner is RecordingException));
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    // Kept apart, so nothing on the test's stack holds the erasure's task when the collector runs.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task AbandonAFailingErasureAsync()
    {
        var sink = new GatedThrowingSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            using (var cancel = new CancellationTokenSource())
            {
                var erase = recorder.EraseAsync("customer-7", cancel.Token).AsTask();
                await sink.Entered.Task.WaitAsync(Bound);
                await cancel.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => erase.WaitAsync(Bound));
            }

            sink.Gate.SetResult();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }
    }

    private static void AssertTimedOut(FakeLogCollector logs, int records, int outcomes, int erasures, int flushes)
    {
        var timedOut = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1012);
        Assert.Equal(LogLevel.Error, timedOut.Level);
        Assert.Equal(records.ToString(CultureInfo.InvariantCulture), Field(timedOut, "Records"));
        Assert.Equal(outcomes.ToString(CultureInfo.InvariantCulture), Field(timedOut, "Outcomes"));
        Assert.Equal(erasures.ToString(CultureInfo.InvariantCulture), Field(timedOut, "Erasures"));
        Assert.Equal(flushes.ToString(CultureInfo.InvariantCulture), Field(timedOut, "Flushes"));
        Assert.DoesNotContain("SENTINEL", timedOut.Message, StringComparison.Ordinal);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Bound;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
            await Task.Yield();
        }
    }

    // An erasure that waits at a gate, then throws.
    private sealed class GatedThrowingSink : IDecisionRecordSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Gate.Task;
            throw new IOException("The disk is gone.");
        }

        public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TheQueueLengthGauge_ReportsQueuedItems()
    {
        using var metrics = new MetricCapture();
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            Assert.Equal(0, metrics.QueueLength());
            recorder.TryEnqueue(Record("d-1"));
            await sink.WaitEnteredAsync();
            recorder.TryEnqueue(Record("d-2"));
            await recorder.AddOutcomeAsync("d-2", Signal());

            Assert.Equal(2, metrics.QueueLength());

            sink.Release();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            Assert.Equal(0, metrics.QueueLength());
        }
    }

    [Fact]
    public void TheArguments_AreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new DecisionRecorder(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingOptions { Capacity = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingOptions { FlushTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingOptions { FlushTimeout = Timeout.InfiniteTimeSpan });
        var defaults = new RecordingOptions();
        Assert.Equal(1024, defaults.Capacity);
        Assert.Equal(TimeSpan.FromSeconds(5), defaults.FlushTimeout);
    }

    [Fact]
    public async Task TheRecorder_KeepsItsTimeProvider_AndChecksItsArguments()
    {
        var time = new FakeTimeProvider();
        await using var recorder = new DecisionRecorder(new RecordingSpySink(), timeProvider: time);
        Assert.Same(time, recorder.Time);
        await using var system = new DecisionRecorder(new RecordingSpySink());
        Assert.Same(TimeProvider.System, system.Time);

        Assert.Throws<ArgumentNullException>(() => recorder.TryEnqueue(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => recorder.AddOutcomeAsync("", Signal()).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => recorder.AddOutcomeAsync("d-1", null!).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => recorder.EraseAsync("").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => recorder.EraseDecisionAsync(null!).AsTask());
    }
}
