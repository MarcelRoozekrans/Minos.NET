using System.Diagnostics.Metrics;
using System.Reflection;

namespace Minos.Recording;

/// <summary>
/// The recorder's metrics, on a meter named <c>Minos</c> at the package's version, as the core's are: counters of
/// records, outcomes and erasures by <c>result</c>, where every record handed to the recorder ends in exactly one result;
/// the tag-less observable counters
/// <c>minos.recording.log_failures</c>, of log events a logger threw on, and <c>minos.recording.record_failures</c>, of
/// decisions the recording stage failed to record; and the gauge <c>minos.recording.queue_length</c>, summed over every
/// recorder in the process. The tags are fixed values: never a key, an id or the state.
/// </summary>
/// <remarks>
/// The two failure counters are observable, over totals kept with <see cref="Interlocked"/>: counting a failure never
/// calls a listener, so a listener that throws cannot make the counting of a failure fail too. Their callbacks run when a
/// listener collects, never on a decision's path.
/// </remarks>
internal static class RecordingTelemetry
{
    internal const string MeterName = "Minos";

    internal const string Written = "written";
    internal const string DroppedFull = "dropped_full";
    internal const string DroppedRedaction = "dropped_redaction";
    internal const string RedactionFailed = "redaction_failed";
    internal const string SinkFailed = "sink_failed";
    internal const string DroppedDisposed = "dropped_disposed";
    internal const string DroppedFlushTimeout = "dropped_flush_timeout";
    internal const string Failed = "failed";

    private const string ResultTag = "result";

    // The core's meter carries its package's informational version, and every Minos package shares one version, so a
    // listener sees the meter named Minos at one version. Reading an assembly attribute is safe under trimming and AOT.
    private static readonly Meter Meter = new(
        MeterName, typeof(RecordingTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private static readonly Counter<long> Records = Meter.CreateCounter<long>(
        "minos.recording.records", "{record}", "Records by result: written, dropped_full, dropped_redaction, redaction_failed, sink_failed, dropped_disposed or dropped_flush_timeout.");

    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "minos.recording.outcomes", "{outcome}", "Outcomes by result: written or failed.");

    private static readonly Counter<long> Erasures = Meter.CreateCounter<long>(
        "minos.recording.erasures", "{erasure}", "Erasures by result: written or failed.");

    private static long s_queued;
    private static long s_logFailures;
    private static long s_recordFailures;

    // Created with the counters; the meter keeps them and calls them when a listener collects.
    private static readonly ObservableCounter<long> LogFailures = Meter.CreateObservableCounter(
        "minos.recording.log_failures", static () => Volatile.Read(ref s_logFailures), "{event}", "Recorder log events lost because the logger threw.");

    private static readonly ObservableCounter<long> RecordFailures = Meter.CreateObservableCounter(
        "minos.recording.record_failures", static () => Volatile.Read(ref s_recordFailures), "{decision}", "Decisions the recording stage failed to record, for example because a metric listener threw.");

    private static readonly ObservableGauge<long> QueueLength = Meter.CreateObservableGauge(
        "minos.recording.queue_length", static () => Math.Max(0, Volatile.Read(ref s_queued)), "{item}", "Items waiting in the recorders' queues.");

    /// <summary>Counts a record with its result.</summary>
    internal static void Record(string result) => Records.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    /// <summary>Counts an outcome with its result.</summary>
    internal static void Outcome(string result) => Outcomes.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    /// <summary>Counts an erasure with its result.</summary>
    internal static void Erasure(string result) => Erasures.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    /// <summary>Counts a log event the logger threw on. It cannot throw.</summary>
    internal static void LogFailed() => Interlocked.Increment(ref s_logFailures);

    /// <summary>Counts a decision the recording stage failed to record. It cannot throw.</summary>
    internal static void RecordFailed() => Interlocked.Increment(ref s_recordFailures);

    /// <summary>Adds to the queue length: 1 when an item is queued, -1 when it leaves the queue.</summary>
    internal static void Queued(long delta) => Interlocked.Add(ref s_queued, delta);
}
