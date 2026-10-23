using System.Diagnostics.Metrics;

namespace Minos.Recording.Tests;

/// <summary>
/// Reads the <c>minos.recording.*</c> observable counters on the "Minos" meter. Their totals are kept for the whole
/// process, so a sum is the change since the capture was created. It also counts any measurement that reaches the
/// listener outside a collection, which an observable counter never makes.
/// </summary>
internal sealed class MetricCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly Dictionary<(string Instrument, string? Result), long> _latest = [];
    private readonly Dictionary<(string Instrument, string? Result), long> _baseline;
    private bool _collecting;
    private int _outside;

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

            lock (_latest)
            {
                if (!_collecting)
                {
                    _outside++;
                    return;
                }

                _latest[(instrument.Name, result)] = value;
            }
        });
        _listener.Start();
        _baseline = Collect();
    }

    /// <summary>Gets how many measurements reached the listener outside a collection: on a decision's path, say.</summary>
    public int MeasurementsOutsideCollection
    {
        get
        {
            lock (_latest)
            {
                return _outside;
            }
        }
    }

    /// <summary>How much a counter's total with a <c>result</c> tag grew since the capture was created.</summary>
    public long Sum(string instrument, string result)
    {
        var now = Collect();
        return now.GetValueOrDefault((instrument, result)) - _baseline.GetValueOrDefault((instrument, result));
    }

    /// <summary>How much a counter's total grew since the capture was created, whatever its tags.</summary>
    public long Sum(string instrument)
    {
        var now = Collect();
        return now.Where(pair => string.Equals(pair.Key.Instrument, instrument, StringComparison.Ordinal)).Sum(pair => pair.Value)
            - _baseline.Where(pair => string.Equals(pair.Key.Instrument, instrument, StringComparison.Ordinal)).Sum(pair => pair.Value);
    }

    public void Dispose() => _listener.Dispose();

    private Dictionary<(string Instrument, string? Result), long> Collect()
    {
        lock (_latest)
        {
            _latest.Clear();
            _collecting = true;
        }

        try
        {
            _listener.RecordObservableInstruments();
        }
        finally
        {
            lock (_latest)
            {
                _collecting = false;
            }
        }

        lock (_latest)
        {
            return new(_latest);
        }
    }
}
