using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Minos.Docs.Tests;

#region Observability_OpenTelemetryNames
// The two names an OpenTelemetry setup needs. The source carries the spans and the meter carries the metrics.
public static class JevTelemetryNames
{
    public const string Source = "Minos";

    public const string Meter = "Minos";
}
#endregion

public static class ObservabilityOpenTelemetry
{
    #region Observability_OpenTelemetryWiring
    // Needs the OpenTelemetry.Extensions.Hosting package. Add an exporter to each builder for where the data should go.
    public static IServiceCollection AddJevTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(JevTelemetryNames.Source))
            .WithMetrics(metrics => metrics.AddMeter(JevTelemetryNames.Meter));

        return services;
    }
    #endregion
}

public sealed class ObservabilityOpenTelemetryTests
{
    [Fact]
    public void TheWiring_SubscribesOpenTelemetryToJevsSourceAndMeter()
    {
        // Metrics need a reader before OpenTelemetry enables an instrument, as an exporter would bring.
        using var provider = new ServiceCollection()
            .AddJevTelemetry()
            .ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddReader(new BaseExportingMetricReader(new NoExporter())))
            .BuildServiceProvider();
        _ = provider.GetRequiredService<TracerProvider>();
        _ = provider.GetRequiredService<MeterProvider>();

        using var source = new ActivitySource(JevTelemetryNames.Source);
        using var meter = new Meter(JevTelemetryNames.Meter);
        var counter = meter.CreateCounter<long>("docs.probe");

        Assert.True(source.HasListeners());
        Assert.True(counter.Enabled);
    }
}

internal sealed class NoExporter : BaseExporter<Metric>
{
    public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
}
