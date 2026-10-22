using System.Diagnostics.Metrics;

namespace Minos.Recording.Tests;

/// <summary>Captures the <c>minos.recording.*</c> measurements on the "Minos" meter while it lives.</summary>
internal sealed class MetricCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, long Value, string? Result)> _measurements = [];

    public MetricCapture()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, "Minos", StringComparison.Ordinal)
                && instrument.Name.StartsWith("minos.recording.", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? result = null;
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "result", StringComparison.Ordinal))
                {
                    result = tag.Value as string;
                }
            }

            lock (_measurements)
            {
                _measurements.Add((instrument.Name, value, result));
            }
        });
        _listener.Start();
    }

    /// <summary>The sum of a counter's measurements with a <c>result</c> tag.</summary>
    public long Sum(string instrument, string result)
    {
        lock (_measurements)
        {
            return _measurements
                .Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal) && string.Equals(m.Result, result, StringComparison.Ordinal))
                .Sum(m => m.Value);
        }
    }

    /// <summary>The queue length gauge, read now.</summary>
    public long QueueLength()
    {
        lock (_measurements)
        {
            _measurements.RemoveAll(m => string.Equals(m.Instrument, "minos.recording.queue_length", StringComparison.Ordinal));
        }

        _listener.RecordObservableInstruments();
        lock (_measurements)
        {
            return _measurements.Single(m => string.Equals(m.Instrument, "minos.recording.queue_length", StringComparison.Ordinal)).Value;
        }
    }

    public void Dispose() => _listener.Dispose();
}
