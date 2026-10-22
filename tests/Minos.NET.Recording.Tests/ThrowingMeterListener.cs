using System.Diagnostics.Metrics;

namespace Minos.Recording.Tests;

/// <summary>A meter listener that throws on every measurement of the named instrument, as a broken exporter would.</summary>
internal sealed class ThrowingMeterListener : IDisposable
{
    private readonly MeterListener _listener = new();

    public ThrowingMeterListener(string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, "Minos", StringComparison.Ordinal)
                && string.Equals(instrument.Name, instrumentName, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidCastException("The exporter is down."));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();
}
