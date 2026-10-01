using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ZeroAlloc.Jev.Shared;

/// <summary>
/// Listens to the ZeroAlloc.Jev source and meter, records every span and enables every instrument, and discards it all,
/// so every tag, deferred read and measurement runs. Compiled into the AOT smoke app and, linked, into the benchmarks.
/// </summary>
internal sealed class DiscardingTelemetry : IDisposable
{
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;
    private long _measurements;

    public DiscardingTelemetry()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = static source => source.Name is "ZeroAlloc.Jev",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_activities);

        _meters = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name is "ZeroAlloc.Jev")
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

    public void Dispose()
    {
        _meters.Dispose();
        _activities.Dispose();
    }
}
