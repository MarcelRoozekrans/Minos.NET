namespace ZeroAlloc.Jev.Docs.Tests;

#region Observability_OpenTelemetryNames
// The two names an OpenTelemetry setup needs. The source carries the spans and the meter carries the metrics.
// With the OpenTelemetry.Extensions.Hosting package an application passes them on like this:
//   services.AddOpenTelemetry()
//       .WithTracing(tracing => tracing.AddSource(JevTelemetryNames.Source))
//       .WithMetrics(metrics => metrics.AddMeter(JevTelemetryNames.Meter));
public static class JevTelemetryNames
{
    public const string Source = "ZeroAlloc.Jev";

    public const string Meter = "ZeroAlloc.Jev";
}
#endregion
