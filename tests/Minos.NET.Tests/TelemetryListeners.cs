namespace Minos.Tests;

/// <summary>
/// The collection every test class joins when it attaches an <see cref="System.Diagnostics.ActivityListener"/> or a
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>, or measures allocations that one would change. Listeners are
/// process-wide, so this collection runs alone, after every parallel one.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryListeners
{
    /// <summary>The collection's name, for <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Telemetry listeners";
}
