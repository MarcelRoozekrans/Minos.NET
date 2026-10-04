namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>The mock's ceiling: the best throughput phase over several worker counts, and the count that reached it.</summary>
/// <param name="PerSecond">The highest completed calls per second of any phase.</param>
/// <param name="Workers">The worker count of that phase.</param>
public sealed record CeilingMeasurement(double PerSecond, int Workers);

/// <summary>
/// Measures the mock's ceiling as the best of several worker counts. One count alone is not an upper bound: too few
/// workers leave the mock idle, and too many cost the client's cores more than they load the mock.
/// </summary>
public static class CeilingRunner
{
    /// <summary>Runs one phase per worker count, in order, and keeps the fastest.</summary>
    /// <param name="workerCounts">The worker counts to try; at least one.</param>
    /// <param name="runPhase">Runs one measured phase at the given worker count.</param>
    /// <returns>The fastest phase's rate and its worker count.</returns>
    /// <exception cref="ArgumentException"><paramref name="workerCounts"/> is empty.</exception>
    public static async Task<CeilingMeasurement> RunAsync(IReadOnlyList<int> workerCounts, Func<int, Task<ThroughputPhase>> runPhase)
    {
        ArgumentNullException.ThrowIfNull(workerCounts);
        ArgumentNullException.ThrowIfNull(runPhase);
        if (workerCounts.Count == 0)
        {
            throw new ArgumentException("At least one worker count is needed.", nameof(workerCounts));
        }

        CeilingMeasurement? best = null;
        foreach (var workers in workerCounts)
        {
            var phase = await runPhase(workers).ConfigureAwait(false);
            if (best is null || phase.PerSecond > best.PerSecond)
            {
                best = new CeilingMeasurement(phase.PerSecond, workers);
            }
        }

        return best!;
    }
}
