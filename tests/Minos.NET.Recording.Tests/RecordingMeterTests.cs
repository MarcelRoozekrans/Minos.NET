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
}
