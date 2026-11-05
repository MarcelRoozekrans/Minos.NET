using System.Diagnostics.Metrics;
using System.Reflection;

namespace Minos.Telemetry;

/// <summary>
/// The fallback stage's counter, <c>minos.fallbacks</c>, on a meter named <c>Minos</c> at the package's version, as the
/// telemetry stage's is. The stage emits it itself, once per request that went past step 0, for an answer and for an
/// exhausted chain alike: an exhausted chain has no response to carry it, and a cached copy of an answer, which no
/// stage builds, cannot count it twice. The tags are the answering step's name and a fixed outcome, never the state.
/// </summary>
/// <remarks>Recording checks <see cref="Instrument.Enabled"/> first, so with nothing listening a fallback allocates nothing here.</remarks>
internal static class FallbackTelemetry
{
    // Every Minos package shares one version, so a listener sees the meter named Minos at one version. Reading an
    // assembly attribute is safe under trimming and AOT.
    private static readonly Meter Meter = new(
        DecisionTelemetry.SourceName,
        typeof(FallbackTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private static readonly Counter<long> Counter = Meter.CreateCounter<long>(
        DecisionTelemetry.FallbackCounter, DecisionTelemetry.Fallbacks, "Requests that went past the first step of a fallback chain, by outcome.");

    /// <summary>Counts a request that a step after step 0 answered.</summary>
    /// <param name="step">The answering step's name.</param>
    internal static void Answered(string step)
    {
        if (Counter.Enabled)
        {
            Counter.Add(
                1,
                new KeyValuePair<string, object?>(DecisionTelemetry.FallbackOutcome, DecisionTelemetry.FallbackAnswered),
                new KeyValuePair<string, object?>(DecisionTelemetry.FallbackClient, step));
        }
    }

    /// <summary>Counts a request that went past step 0 and got no answer.</summary>
    internal static void Exhausted()
    {
        if (Counter.Enabled)
        {
            Counter.Add(1, new KeyValuePair<string, object?>(DecisionTelemetry.FallbackOutcome, DecisionTelemetry.FallbackExhausted));
        }
    }
}
