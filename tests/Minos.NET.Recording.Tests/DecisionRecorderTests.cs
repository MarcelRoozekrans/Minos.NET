using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using static Minos.Recording.Tests.TestRecords;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class DecisionRecorderTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static (DecisionRecorder Recorder, FakeLogCollector Logs, ILoggerFactory Factory) Create(IDecisionRecordSink sink, RecordingOptions? options = null)
    {
        var logs = new FakeLogCollector();
        var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new FakeLoggerProvider(logs)));
        return (new DecisionRecorder(sink, options, factory), logs, factory);
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
        var sink = new BlockingSink(honoursCancellation: false);
        var (recorder, logs, factory) = Create(sink, new RecordingOptions { FlushTimeout = TimeSpan.FromMilliseconds(100) });
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

                var watch = Stopwatch.StartNew();
                await recorder.DisposeAsync().AsTask().WaitAsync(Bound);
                watch.Stop();

                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"DisposeAsync took {watch.Elapsed}.");
                foreach (var lost in new[] { byKey, byId, flush })
                {
                    var exception = await Assert.ThrowsAsync<RecordingException>(() => lost.WaitAsync(Bound));
                    Assert.DoesNotContain("SENTINEL", exception.Message, StringComparison.Ordinal);
                }

                Assert.Equal(0, metrics.QueueLength());
            }

            var timedOut = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1012);
            Assert.Equal(LogLevel.Error, timedOut.Level);
            Assert.Equal("100", Field(timedOut, "TimeoutMs"));
            Assert.Equal("2", Field(timedOut, "Records"));
            Assert.Equal("1", Field(timedOut, "Outcomes"));
            Assert.Equal("2", Field(timedOut, "Erasures"));
            Assert.Equal("1", Field(timedOut, "Flushes"));
            Assert.DoesNotContain("SENTINEL", timedOut.Message, StringComparison.Ordinal);
        }
        finally
        {
            sink.Release();
        }
    }

    [Fact]
    public async Task Calls_AfterDispose_Throw_AndTryEnqueueReturnsFalse()
    {
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
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
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
