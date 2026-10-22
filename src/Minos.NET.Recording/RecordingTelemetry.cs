using System.Diagnostics.Metrics;

namespace Minos.Recording;

/// <summary>
/// The recorder's metrics, on a meter named <c>Minos</c> as the core's are: counters of records, outcomes and erasures by
/// <c>result</c>, where every record handed to the recorder ends in exactly one result, the tag-less counter
/// <c>minos.recording.log_failures</c> of log events a logger threw on, and the gauge <c>minos.recording.queue_length</c>, summed over every recorder in the process. The tags
/// are fixed values: never a key, an id or the state.
/// </summary>
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

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Records = Meter.CreateCounter<long>(
        "minos.recording.records", "{record}", "Records by result: written, dropped_full, dropped_redaction, redaction_failed, sink_failed, dropped_disposed or dropped_flush_timeout.");

    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "minos.recording.outcomes", "{outcome}", "Outcomes by result: written or failed.");

    private static readonly Counter<long> Erasures = Meter.CreateCounter<long>(
        "minos.recording.erasures", "{erasure}", "Erasures by result: written or failed.");

    private static readonly Counter<long> LogFailures = Meter.CreateCounter<long>(
        "minos.recording.log_failures", "{event}", "Recorder log events lost because the logger threw.");

    private static long s_queued;

    // Created with the counters; the meter keeps it and calls it when a listener reads the gauges.
    private static readonly ObservableGauge<long> QueueLength = Meter.CreateObservableGauge(
        "minos.recording.queue_length", static () => Math.Max(0, Volatile.Read(ref s_queued)), "{item}", "Items waiting in the recorders' queues.");

    /// <summary>Counts a record with its result.</summary>
    internal static void Record(string result) => Records.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    /// <summary>Counts an outcome with its result.</summary>
    internal static void Outcome(string result) => Outcomes.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    /// <summary>Counts an erasure with its result.</summary>
    internal static void Erasure(string result) => Erasures.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    /// <summary>Counts a log event the logger threw on.</summary>
    internal static void LogFailed() => LogFailures.Add(1);

    /// <summary>Adds to the queue length: 1 when an item is queued, -1 when it leaves the queue.</summary>
    internal static void Queued(long delta) => Interlocked.Add(ref s_queued, delta);
}
