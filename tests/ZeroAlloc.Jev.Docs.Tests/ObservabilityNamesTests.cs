using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace ZeroAlloc.Jev.Docs.Tests;

// Listeners are process-wide, so this runs with the other tests that attach one.
[Collection(DocsTelemetryListeners.Name)]
public sealed class ObservabilityNamesTests
{
    private const string UrgencyResponse = """
        { "model": "jev-1.13.0", "answers": { "is_urgent": { "type": "noul", "noul": 0.93 } }, "usage": { "input_tokens": 41, "output_tokens": 3 } }
        """;

    // The names the page tells you to subscribe to are the ones the library really publishes, with the package's version.
    [Fact]
    public async Task TheNamesOnThePage_AreTheSourceAndMeterTheClientPublishes_AtThePackagesVersion()
    {
        var spanSources = new List<ActivitySource>();
        var meters = new List<Meter>();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, JevTelemetryNames.Source, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span =>
            {
                lock (spanSources)
                {
                    spanSources.Add(span.Source);
                }
            },
        };
        ActivitySource.AddActivityListener(activities);
        using var instruments = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, JevTelemetryNames.Meter, StringComparison.Ordinal))
                {
                    lock (meters)
                    {
                        meters.Add(instrument.Meter);
                    }

                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        instruments.Start();

        var (http, jev, _) = ScriptedJev.Client(ScriptedJev.Quick(0), Reply.Ok(UrgencyResponse));
        using (http)
        using (jev)
        {
            await RawRequests.UrgencyAsync(jev, "Help!", CancellationToken.None);
        }

        var version = typeof(JevClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.False(string.IsNullOrEmpty(version));
        Assert.Equal(version, Only.Of(spanSources).Version);
        Assert.Equal(version, Only.Of(meters.Select(m => m.Version).Distinct(StringComparer.Ordinal)));
    }

    // The library names its source and meter in one constant, the page's two constants repeat it.
    [Fact]
    public void TheNamesOnThePage_AreTheLibrariesConstant()
    {
        var telemetry = File.ReadAllText(Path.Combine(PublishedPages.Root, "src", "ZeroAlloc.Jev", "Telemetry", "JevTelemetry.cs"));
        var operations = File.ReadAllText(Path.Combine(PublishedPages.Root, "src", "ZeroAlloc.Jev", "Telemetry", "IJevOperations.cs"));

        Assert.Contains($"SourceName = \"{JevTelemetryNames.Source}\"", telemetry, StringComparison.Ordinal);
        Assert.Equal(JevTelemetryNames.Source, JevTelemetryNames.Meter);
        Assert.Contains("[Instrument(SourceName)]", operations, StringComparison.Ordinal);
    }

    // The page says neither package depends on OpenTelemetry.
    [Theory]
    [InlineData("ZeroAlloc.Jev")]
    [InlineData("ZeroAlloc.Jev.DependencyInjection")]
    public void ThePackages_TakeNoOpenTelemetryDependency(string project)
    {
        var projectFile = File.ReadAllText(Path.Combine(PublishedPages.Root, "src", project, project + ".csproj"));
        var packages = File.ReadAllText(Path.Combine(PublishedPages.Root, "Directory.Packages.props"));

        Assert.DoesNotContain("OpenTelemetry", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenTelemetry", packages, StringComparison.OrdinalIgnoreCase);
    }
}
