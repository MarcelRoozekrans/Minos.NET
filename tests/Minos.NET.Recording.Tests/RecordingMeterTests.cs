using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Minos.Recording.Tests;

/// <summary>The recorder's instruments are on a meter named <c>Minos</c> with the same version as the core's meter, so a
/// listener sees one meter name at one version, as the observability page says.</summary>
public sealed class RecordingMeterTests
{
    [Fact]
    public void TheRecordersMeter_IsMinos_AtThePackagesVersion()
    {
        RuntimeHelpers.RunClassConstructor(typeof(RecordingTelemetry).TypeHandle);
        var meters = new List<Meter>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Name.StartsWith("minos.recording.", StringComparison.Ordinal))
                {
                    lock (meters)
                    {
                        meters.Add(instrument.Meter);
                    }
                }
            },
        };
        listener.Start();

        var version = typeof(DecisionRequest).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var meter = Assert.Single(meters.Distinct());
        Assert.Equal("Minos", meter.Name);
        Assert.False(string.IsNullOrEmpty(version));
        Assert.Equal(version, meter.Version);
    }

    [Fact]
    public void ShadowsCounter_IsPublishedWithItsResults()
    {
        RuntimeHelpers.RunClassConstructor(typeof(RecordingTelemetry).TypeHandle);
        Instrument? shadows = null;
        var results = new List<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, "Minos", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "minos.recording.shadows", StringComparison.Ordinal))
                {
                    shadows = instrument;
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "result", StringComparison.Ordinal))
                {
                    results.Add(tag.Value as string);
                }
            }
        });
        listener.Start();

        listener.RecordObservableInstruments();

        Assert.NotNull(shadows);
        Assert.IsType<ObservableCounter<long>>(shadows);
        Assert.Equal("{shadow}", shadows.Unit);
        Assert.Equal("Shadow writes by result: written, failed, dropped, skipped_cap or skipped_busy.", shadows.Description);
        Assert.Equal(["written", "failed", "dropped", "skipped_cap", "skipped_busy"], results);
    }
}
