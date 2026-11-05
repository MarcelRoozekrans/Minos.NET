using System.Diagnostics;
using System.Globalization;
using Minos.Benchmarks.Compare.Adapters;

namespace Minos.Benchmarks.Compare;

/// <summary>One client's latency calls and figures.</summary>
/// <param name="Calls">Every call the client made, warm-up included.</param>
/// <param name="Latency">The mean, median and 99th percentile of the timed calls, in milliseconds.</param>
public sealed record LatencyRun(long Calls, LatencyFigures Latency);

/// <summary>
/// Times the clients one call at a time, the same way in every harness: warm-up calls, then timed calls, each timed on
/// its own; the figures are taken over the individual call times. The .NET harness measures five clients in one
/// process, so it interleaves them: in each round every client makes a block of timed calls, in an order that rotates
/// by round, and each client's times are pooled over all its rounds. No client always runs first or last, and a slow
/// spell on the machine falls on every client instead of on whichever was being measured.
/// </summary>
public static class LatencyRunner
{
    /// <summary>
    /// Makes <paramref name="warmupCalls"/> untimed calls per client, then <paramref name="rounds"/> rounds in which each
    /// client times <paramref name="callsPerRound"/> calls one by one. Round <c>r</c> starts with the client at
    /// <c>(rotation + r) mod count</c>, as <see cref="Order"/> gives. The mock's count is read around every block of
    /// one client's calls, and each client must have sent exactly one request per call over all its blocks.
    /// </summary>
    /// <param name="adapters">The clients to call, in their base order.</param>
    /// <param name="warmupCalls">How many calls each client makes before timing.</param>
    /// <param name="rounds">How many rounds to run.</param>
    /// <param name="callsPerRound">How many calls each client times in each round.</param>
    /// <param name="rotation">Where the first round starts in the base order.</param>
    /// <param name="readCount">Reads the mock's served-request count.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <returns>Each client's calls and figures, by client name.</returns>
    /// <exception cref="InvalidOperationException">
    /// A call read an answer other than the recorded one, or a client's requests differ from its calls.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, LatencyRun>> RunRoundsAsync(
        IReadOnlyList<IClientAdapter> adapters,
        int warmupCalls,
        int rounds,
        int callsPerRound,
        int rotation,
        Func<CancellationToken, Task<long>> readCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(readCount);
        if (adapters.Count == 0)
        {
            throw new ArgumentException("There are no clients.", nameof(adapters));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(warmupCalls);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(callsPerRound, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(rotation);

        var calls = new long[adapters.Count];
        var served = new long[adapters.Count];
        var times = Enumerable.Range(0, adapters.Count).Select(_ => new List<double>(rounds * callsPerRound)).ToArray();

        foreach (var i in Order(adapters.Count, 0, rotation))
        {
            var adapter = adapters[i];
            await CountedAsync(i, async () =>
            {
                for (var call = 0; call < warmupCalls; call++)
                {
                    Check(adapter, await adapter.CallAsync(cancellationToken).ConfigureAwait(false));
                }

                return warmupCalls;
            }).ConfigureAwait(false);
        }

        for (var round = 0; round < rounds; round++)
        {
            foreach (var i in Order(adapters.Count, round, rotation))
            {
                var adapter = adapters[i];
                var into = times[i];
                await CountedAsync(i, async () =>
                {
                    for (var call = 0; call < callsPerRound; call++)
                    {
                        var start = Stopwatch.GetTimestamp();
                        var outcome = await adapter.CallAsync(cancellationToken).ConfigureAwait(false);
                        into.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                        Check(adapter, outcome);
                    }

                    return callsPerRound;
                }).ConfigureAwait(false);
            }
        }

        var runs = new Dictionary<string, LatencyRun>(StringComparer.Ordinal);
        for (var i = 0; i < adapters.Count; i++)
        {
            if (served[i] != calls[i])
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{adapters[i].Client}: the latency rounds made {calls[i]} calls but the mock served {served[i]} requests. A retry or an extra request breaks the one-request-per-call rule."));
            }

            runs[adapters[i].Client] = new LatencyRun(calls[i], Summarize(times[i]));
        }

        return runs;

        // Reads the count around one client's block, so its requests are told apart from the others'.
        async Task CountedAsync(int client, Func<Task<int>> makeCalls)
        {
            var before = await readCount(cancellationToken).ConfigureAwait(false);
            calls[client] += await makeCalls().ConfigureAwait(false);
            served[client] += await readCount(cancellationToken).ConfigureAwait(false) - before;
        }
    }

    /// <summary>The order of the clients in one round: the base order, rotated to start at <c>(rotation + round) mod count</c>.</summary>
    /// <param name="count">How many clients there are.</param>
    /// <param name="round">The round, from 0.</param>
    /// <param name="rotation">Where round 0 starts.</param>
    /// <returns>The clients' indexes in the base order, in the order they run.</returns>
    public static IReadOnlyList<int> Order(int count, int round, int rotation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(round);
        ArgumentOutOfRangeException.ThrowIfNegative(rotation);
        var order = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = (int)(((long)rotation + round + i) % count);
        }

        return order;
    }

    /// <summary>
    /// The arithmetic mean of <paramref name="milliseconds"/>, and its median and 99th percentile by nearest rank.
    /// </summary>
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
