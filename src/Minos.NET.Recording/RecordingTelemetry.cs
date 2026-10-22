using System.Diagnostics.Metrics;
using System.Reflection;

namespace Minos.Recording;

/// <summary>
/// The recorder's metrics, on a meter named <c>Minos</c> at the package's version, as the core's are: four observable
/// counters over totals kept with <see cref="Interlocked"/>. Records, outcomes and erasures are counted by
/// <c>result</c>, and <c>minos.recording.log_failures</c>, with no tags, counts log events a logger threw on. Counting
/// never calls a listener: the callbacks run when a listener collects, never on a decision's path. The tags are fixed
/// values, never a key, an id or the state.
/// </summary>
internal static class RecordingTelemetry
{
    internal const string MeterName = "Minos";

    internal const string Written = "written";
    internal const string Dropped = "dropped";
    internal const string Failed = "failed";

    /// <summary>Records by result: <c>written</c>, <c>dropped</c> or <c>failed</c>.</summary>
    internal static readonly Totals Records = new(Written, Dropped, Failed);

    /// <summary>Outcomes by result: <c>written</c> or <c>failed</c>.</summary>
    internal static readonly Totals Outcomes = new(Written, Failed);

    /// <summary>Erasures by result: <c>written</c> or <c>failed</c>.</summary>
    internal static readonly Totals Erasures = new(Written, Failed);

    private static long s_logFailures;

    // The core's meter carries its package's informational version, and every Minos package shares one version, so a
    // listener sees the meter named Minos at one version. Reading an assembly attribute is safe under trimming and AOT.
    private static readonly Meter Meter = new(
        MeterName, typeof(RecordingTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    // Created with the totals; the meter keeps them and calls them when a listener collects.
    private static readonly ObservableCounter<long> RecordsCounter = Meter.CreateObservableCounter(
        "minos.recording.records", Records.Observe, "{record}", "Records by result: written, dropped or failed.");

    private static readonly ObservableCounter<long> OutcomesCounter = Meter.CreateObservableCounter(
        "minos.recording.outcomes", Outcomes.Observe, "{outcome}", "Outcomes by result: written or failed.");

    private static readonly ObservableCounter<long> ErasuresCounter = Meter.CreateObservableCounter(
        "minos.recording.erasures", Erasures.Observe, "{erasure}", "Erasures by result: written or failed.");

    private static readonly ObservableCounter<long> LogFailuresCounter = Meter.CreateObservableCounter(
        "minos.recording.log_failures", static () => Volatile.Read(ref s_logFailures), "{event}", "Recorder log events lost because the logger threw.");

    /// <summary>Counts a log event the logger threw on.</summary>
    internal static void LogFailed() => Interlocked.Increment(ref s_logFailures);

    /// <summary>A total per <c>result</c> value. Adding to it cannot throw.</summary>
    internal sealed class Totals(params string[] results)
    {
        private readonly long[] _counts = new long[results.Length];

        /// <summary>Counts one item with <paramref name="result"/>, which is one of the values the totals were made with.</summary>
        public void Add(string result) => Interlocked.Increment(ref _counts[Array.IndexOf(results, result)]);

        public Measurement<long>[] Observe()
        {
            var measurements = new Measurement<long>[results.Length];
            for (var i = 0; i < results.Length; i++)
            {
                measurements[i] = new Measurement<long>(Volatile.Read(ref _counts[i]), new KeyValuePair<string, object?>("result", results[i]));
            }

            return measurements;
        }
    }
}
