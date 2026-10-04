using System.Diagnostics;
using ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>The sequential latency loop's calls and figures.</summary>
/// <param name="Calls">Every call the loop made, warm-up included.</param>
/// <param name="Latency">The mean, median and 99th percentile of the timed calls, in milliseconds.</param>
public sealed record LatencyRun(long Calls, LatencyFigures Latency);

/// <summary>
/// Times one client one call at a time, the same way in every harness: warm-up calls, then timed calls, each timed on
/// its own; the figures are taken over the individual call times.
/// </summary>
public static class LatencyRunner
{
    /// <summary>Makes <paramref name="warmupCalls"/> untimed calls, then times <paramref name="timedCalls"/> calls one by one.</summary>
    /// <param name="adapter">The client to call.</param>
    /// <param name="warmupCalls">How many calls to make before timing.</param>
    /// <param name="timedCalls">How many calls to time.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <returns>The calls made and the figures.</returns>
    /// <exception cref="InvalidOperationException">A call read an answer other than the recorded one.</exception>
    public static async Task<LatencyRun> RunAsync(IClientAdapter adapter, int warmupCalls, int timedCalls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupCalls);
        ArgumentOutOfRangeException.ThrowIfLessThan(timedCalls, 1);
        for (var i = 0; i < warmupCalls; i++)
        {
            Check(adapter, await adapter.CallAsync(cancellationToken).ConfigureAwait(false));
        }

        var milliseconds = new List<double>(timedCalls);
        for (var i = 0; i < timedCalls; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var outcome = await adapter.CallAsync(cancellationToken).ConfigureAwait(false);
            milliseconds.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Check(adapter, outcome);
        }

        return new LatencyRun(warmupCalls + timedCalls, Summarize(milliseconds));
    }

    /// <summary>The mean, the median and the 99th percentile of <paramref name="milliseconds"/>, by nearest rank.</summary>
    /// <param name="milliseconds">The individual call times.</param>
    /// <returns>The figures, rounded to the microsecond.</returns>
    /// <exception cref="ArgumentException"><paramref name="milliseconds"/> is empty.</exception>
    public static LatencyFigures Summarize(IReadOnlyCollection<double> milliseconds)
    {
        ArgumentNullException.ThrowIfNull(milliseconds);
        if (milliseconds.Count == 0)
        {
            throw new ArgumentException("There are no call times.", nameof(milliseconds));
        }

        var sorted = milliseconds.Order().ToArray();
        return new LatencyFigures(
            Math.Round(sorted.Average(), 3),
            Math.Round(NearestRank(sorted, 50), 3),
            Math.Round(NearestRank(sorted, 99), 3));
    }

    // The smallest value with at least the given percentage of values at or below it.
    private static double NearestRank(double[] sorted, int percentile)
    {
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
        return sorted[Math.Max(rank, 1) - 1];
    }

    private static void Check(IClientAdapter adapter, CallOutcome outcome)
    {
        if (!outcome.IsExpected)
        {
            throw new InvalidOperationException(adapter.Client + " read an unexpected answer in the latency loop.");
        }
    }
}
