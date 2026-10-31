using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using static Minos.Recording.Tests.TestRecords;

namespace Minos.Recording.Tests;

/// <summary>
/// The recorder's second write of a decision, with its shadow part: it replaces the record, is counted in
/// <c>minos.recording.shadows</c> and never in <c>minos.recording.records</c>, and never lands after an erasure of that
/// decision.
/// </summary>
[Collection(RecordingListeners.Name)]
public sealed class ShadowWriteTests
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
    public async Task ShadowWrite_ReplacesTheRecord()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1");
            Assert.True(recorder.TryEnqueue(record));
            Assert.True(recorder.TryBeginShadow("d-1", record.ErasureKey, out _));

            recorder.EndShadow(record.WithShadow(Shadow()));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        var decision = Assert.Single(sink.Records);
        Assert.NotNull(decision.Record.Shadow);
        Assert.Equal("candidate-served", decision.Record.Shadow.ServedModel);
        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "written"));
        Assert.Equal(1, metrics.Sum("minos.recording.records", "written"));
        Assert.Equal(1, metrics.Sum("minos.recording.records"));
    }

    [Fact]
    public async Task ShadowWrite_KeepsOutcomeQueuedBeforeIt()
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1");
            Assert.True(recorder.TryEnqueue(record));
            Assert.True(recorder.TryBeginShadow("d-1", record.ErasureKey, out _));
            await recorder.AddOutcomeAsync("d-1", Signal());

            recorder.EndShadow(record.WithShadow(Shadow()));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        var decision = Assert.Single(sink.Records);
        Assert.NotNull(decision.Record.Shadow);
        var outcome = Assert.Single(decision.Outcomes);
        Assert.Equal("clicked", Assert.IsType<SignalOutcome>(outcome).Name);
    }

    [Fact]
    public async Task EraseByKey_WhileShadowRuns_DropsTheShadowWrite()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1", erasureKey: "k");
            Assert.True(recorder.TryEnqueue(record));
            Assert.True(recorder.TryBeginShadow("d-1", "k", out _));
            await recorder.EraseAsync("k").AsTask().WaitAsync(Bound);

            recorder.EndShadow(record.WithShadow(Shadow()));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Empty(sink.Records);
        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "dropped"));
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "written"));
    }

    [Fact]
    public async Task EraseById_WhileShadowRuns_DropsTheShadowWrite()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1", erasureKey: "k");
            Assert.True(recorder.TryEnqueue(record));
            Assert.True(recorder.TryBeginShadow("d-1", "k", out _));
            await recorder.EraseDecisionAsync("d-1").AsTask().WaitAsync(Bound);

            recorder.EndShadow(record.WithShadow(Shadow()));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Empty(sink.Records);
        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "dropped"));
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "written"));
    }

    [Fact]
    public async Task EraseOfAnotherKey_KeepsTheShadowWrite()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1", erasureKey: "k");
            Assert.True(recorder.TryEnqueue(record));
            Assert.True(recorder.TryBeginShadow("d-1", "k", out _));
            await recorder.EraseAsync("other").AsTask().WaitAsync(Bound);
            await recorder.EraseDecisionAsync("d-2").AsTask().WaitAsync(Bound);

            recorder.EndShadow(record.WithShadow(Shadow()));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        var decision = Assert.Single(sink.Records);
        Assert.NotNull(decision.Record.Shadow);
        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "written"));
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "dropped"));
    }

    [Fact]
    public async Task EraseByKey_OfARecordWithNoKey_KeepsTheShadowWrite()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1", erasureKey: null);
            Assert.True(recorder.TryEnqueue(record));
            Assert.True(recorder.TryBeginShadow("d-1", null, out _));
            await recorder.EraseAsync("k").AsTask().WaitAsync(Bound);

            recorder.EndShadow(record.WithShadow(Shadow()));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.NotNull(Assert.Single(sink.Records).Record.Shadow);
        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "written"));
    }

    [Fact]
    public async Task FullQueue_DropsTheShadowWrite_WithoutWaiting()
    {
        using var metrics = new MetricCapture();
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink, new RecordingOptions { Capacity = 1 });
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1");
            Assert.True(recorder.TryEnqueue(record));
            await sink.WaitEnteredAsync();
            Assert.True(recorder.TryEnqueue(Record("d-2")));
            Assert.True(recorder.TryBeginShadow("d-1", record.ErasureKey, out _));

            // A void call: it returns at once with the queue still full.
            recorder.EndShadow(record.WithShadow(Shadow()));

            Assert.Equal(1, metrics.Sum("minos.recording.shadows", "dropped"));

            // The drop unregistered the shadow, so the id can be begun again.
            Assert.True(recorder.TryBeginShadow("d-1", record.ErasureKey, out _));
            recorder.AbandonShadow("d-1");
            sink.Release();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(["write:d-1", "write:d-2"], sink.Spy.Calls);
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "written"));
        Assert.Equal(0, metrics.Sum("minos.recording.records", "dropped"));
    }

    [Fact]
    public async Task SinkThrowsOnShadowWrite_CountsFailed_AndKeepsTheFirstRecord()
    {
        using var metrics = new MetricCapture();
        var writes = 0;
        var sink = new ThrowingSink(call => string.Equals(call, "write:d-1", StringComparison.Ordinal) && Interlocked.Increment(ref writes) == 2);
        var (recorder, logs, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            var record = Record("d-1");
            Assert.True(recorder.TryEnqueue(record));
            Assert.True(recorder.TryBeginShadow("d-1", record.ErasureKey, out _));

            recorder.EndShadow(record.WithShadow(Shadow()));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Single(sink.Thrown);
        Assert.Null(Assert.Single(sink.Spy.Inner.Records).Record.Shadow);
        Assert.Equal(1, metrics.Sum("minos.recording.shadows", "failed"));
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "written"));
        Assert.Equal(1, metrics.Sum("minos.recording.records", "written"));
        Assert.Equal(0, metrics.Sum("minos.recording.records", "failed"));
        var failed = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1010);
        Assert.Equal("write", Field(failed, "Operation"));
        Assert.Equal(nameof(InvalidOperationException), Field(failed, "ExceptionType"));
        Assert.Null(failed.Exception);
        Assert.DoesNotContain(ThrowingSink.MessagePrefix, failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("d-1", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_CancelsStopping_AndRefusesNewShadows()
    {
        using var metrics = new MetricCapture();
        var sink = new BlockingSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        {
            var record = Record("d-1");
            Assert.True(recorder.TryEnqueue(record));
            await sink.WaitEnteredAsync();
            Assert.True(recorder.TryBeginShadow("d-1", record.ErasureKey, out var stopping));
            Assert.True(recorder.TryBeginShadow("d-2", null, out var other));
            Assert.False(stopping.IsCancellationRequested);

            // The writer is still in the sink's call, so the dispose is still draining here.
            var disposing = recorder.DisposeAsync().AsTask();

            Assert.False(disposing.IsCompleted);
            Assert.True(stopping.IsCancellationRequested);
            Assert.True(other.IsCancellationRequested);
            Assert.False(recorder.TryBeginShadow("d-3", null, out _));

            // A candidate that finished anyway, and one that saw the cancellation: both are dropped, neither written.
            recorder.EndShadow(record.WithShadow(Shadow()));
            recorder.AbandonShadow("d-2");

            sink.Release();
            await disposing.WaitAsync(Bound);
        }

        Assert.False(recorder.TryBeginShadow("d-4", null, out _));
        Assert.Equal(["write:d-1"], sink.Spy.Calls);
        Assert.Null(Assert.Single(sink.Spy.Inner.Records).Record.Shadow);
        Assert.Equal(2, metrics.Sum("minos.recording.shadows", "dropped"));
        Assert.Equal(0, metrics.Sum("minos.recording.shadows", "written"));
    }

    [Fact]
    public async Task AbandonShadow_CountsDropped_AndUnregisters()
    {
        using var metrics = new MetricCapture();
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            Assert.True(recorder.TryBeginShadow("d-1", null, out _));

            recorder.AbandonShadow("d-1");

            Assert.Equal(1, metrics.Sum("minos.recording.shadows", "dropped"));
            Assert.True(recorder.TryBeginShadow("d-1", null, out _));
            recorder.AbandonShadow("d-1");
        }
    }

    [Fact]
    public async Task BeginShadowTwiceForOneId_ReturnsFalse()
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Create(sink);
        using (factory)
        await using (recorder)
        {
            Assert.True(recorder.TryBeginShadow("d-1", "k", out _));

            Assert.False(recorder.TryBeginShadow("d-1", "k", out _));

            Assert.True(recorder.TryBeginShadow("d-2", "k", out _));
            recorder.AbandonShadow("d-1");
            recorder.AbandonShadow("d-2");
        }
    }
}
