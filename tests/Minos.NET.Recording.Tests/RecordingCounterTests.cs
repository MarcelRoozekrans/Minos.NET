using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

/// <summary>
/// The recorder's five instruments are observable counters over totals, so counting never calls a listener on a
/// decision's path, and every record, outcome and erasure is counted under one <c>result</c>.
/// </summary>
[Collection(RecordingListeners.Name)]
public sealed class RecordingCounterTests
{
    [Fact]
    public void TheRecorder_PublishesFiveObservableCounters()
    {
        RuntimeHelpers.RunClassConstructor(typeof(RecordingTelemetry).TypeHandle);
        var instruments = new List<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Name.StartsWith("minos.recording.", StringComparison.Ordinal))
                {
                    lock (instruments)
                    {
                        instruments.Add(instrument);
                    }
                }
            },
        };
        listener.Start();

        Assert.Equal(
            ["minos.recording.erasures", "minos.recording.log_failures", "minos.recording.outcomes", "minos.recording.records", "minos.recording.shadows"],
            instruments.Select(instrument => instrument.Name).Order(StringComparer.Ordinal));
        Assert.All(instruments, instrument => Assert.IsType<ObservableCounter<long>>(instrument));
    }

    [Fact]
    public async Task EachResult_IsCountedUnderItsTag_AndNoListenerIsCalledOnTheDecisionPath()
    {
        using var metrics = new MetricCapture();
        var sink = new ThrowingSink(call => call is "write:d-failed" or "outcome:d-failed" or "erase:customer-failed");
        var redact = (DecisionContent state) => state.ToString() switch
        {
            "drop" => RedactionResult.Drop,
            "throw" => throw new FormatException(),
            _ => RedactionResult.Keep(state),
        };
        var (recorder, _, factory) = Recorder(sink, new RecordingOptions { Redact = redact });
        using (factory)
        {
            using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            foreach (var (id, state) in ((string, string)[])[("d-1", "keep"), ("d-failed", "keep"), ("d-drop", "drop"), ("d-throw", "throw")])
            {
                using (DecisionRecording.Begin(id))
                {
                    Assert.True((await client.EvaluateAsync(Request(state))).IsSuccess);
                }
            }

            await recorder.AddOutcomeAsync("d-1", TestRecords.Signal());
            await recorder.AddOutcomeAsync("d-failed", TestRecords.Signal());
            await recorder.EraseAsync("customer-7").AsTask().WaitAsync(Bound);
            await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseAsync("customer-failed").AsTask().WaitAsync(Bound));
            await recorder.DisposeAsync();

            // Handed over after dispose: dropped.
            Assert.True((await client.EvaluateAsync(Request("keep"))).IsSuccess);
        }

        Assert.Equal(0, metrics.MeasurementsOutsideCollection);
        Assert.Equal(1, metrics.Sum("minos.recording.records", "written"));
        Assert.Equal(2, metrics.Sum("minos.recording.records", "dropped"));
        Assert.Equal(2, metrics.Sum("minos.recording.records", "failed"));
        Assert.Equal(1, metrics.Sum("minos.recording.outcomes", "written"));
        Assert.Equal(1, metrics.Sum("minos.recording.outcomes", "failed"));
        Assert.Equal(1, metrics.Sum("minos.recording.erasures", "written"));
        Assert.Equal(1, metrics.Sum("minos.recording.erasures", "failed"));
    }

    [Fact]
    public async Task LogFailures_CountsTheLogEventsALoggerThrewOn()
    {
        using var metrics = new MetricCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new ThrowingLoggerProvider()));
        await using (var recorder = new DecisionRecorder(new RecordingSpySink(), new RecordingOptions { Redact = _ => throw new FormatException() }, factory))
        {
            using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
        }

        Assert.Equal(1, metrics.Sum("minos.recording.log_failures"));
        Assert.Equal(1, metrics.Sum("minos.recording.records", "failed"));
    }
}
