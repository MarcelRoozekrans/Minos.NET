namespace Minos.Docs.Tests;

#region Observability_Listening
using System.Diagnostics;
using System.Diagnostics.Metrics;

public sealed record DecisionMeasurement(string Name, string? Unit, double Value, KeyValuePair<string, object?>[] Tags);

// Listens to everything Jev emits. OpenTelemetry does the same once it is told to add the source and the meter
// named Minos, and then exports what it hears.
public sealed class DecisionTelemetryListener : IDisposable
{
    private const string Name = "Minos";

    private readonly Lock _gate = new();
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters = new();

    public DecisionTelemetryListener()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, Name, StringComparison.Ordinal),

            // The sampler runs when a span starts, and sees the tags the span was created with.
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                lock (_gate)
                {
                    StartTags.Add(options.Tags is { } tags ? [.. tags] : []);
                }

                return ActivitySamplingResult.AllDataAndRecorded;
            },
            ActivityStopped = span =>
            {
                lock (_gate)
                {
                    Spans.Add(span);
                }
            },
        };
        ActivitySource.AddActivityListener(_activities);

        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, Name, StringComparison.Ordinal))
            {
                lock (_gate)
                {
                    Instruments[instrument.Name] = instrument;
                }

                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.Start();
    }

    public IList<Activity> Spans { get; } = [];

    public IList<KeyValuePair<string, object?>[]> StartTags { get; } = [];

    public IList<DecisionMeasurement> Measurements { get; } = [];

    public IDictionary<string, Instrument> Instruments { get; } = new Dictionary<string, Instrument>();

    public void Dispose()
    {
        _meters.Dispose();
        _activities.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        lock (_gate)
        {
            Measurements.Add(new DecisionMeasurement(instrument.Name, instrument.Unit, value, tags.ToArray()));
        }
    }
}
#endregion
