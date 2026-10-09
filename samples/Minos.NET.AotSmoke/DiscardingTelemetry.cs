using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Minos.Shared;

/// <summary>
/// Listens to the Minos source and meter, records every span and enables every instrument, and discards it all,
/// so every tag, deferred read and measurement runs. Compiled into the AOT smoke app and, linked, into the benchmarks.
/// </summary>
internal sealed class DiscardingTelemetry : IDisposable
{
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;
    private long _measurements;
    private long _stoppedSpans;

    public DiscardingTelemetry()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = static source => source.Name is "Minos",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _ => Interlocked.Increment(ref _stoppedSpans),
        };
        ActivitySource.AddActivityListener(_activities);

        _meters = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name is "Minos")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meters.SetMeasurementEventCallback<double>((_, _, _, _) => Interlocked.Increment(ref _measurements));
        _meters.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref _measurements));
        _meters.Start();
    }

    /// <summary>Gets how many measurements arrived, so a check can tell the listeners ran.</summary>
    public long Measurements => Interlocked.Read(ref _measurements);

    /// <summary>Gets how many recorded spans stopped, so a check can tell the activity listener ran.</summary>
    public long StoppedSpans => Interlocked.Read(ref _stoppedSpans);

    public void Dispose()
    {
        _meters.Dispose();
        _activities.Dispose();
    }
}
