using System.Diagnostics;
using Minos.Benchmarks.Compare.Adapters;

namespace Minos.Benchmarks.Compare;

/// <summary>The calls one throughput phase made, and how long it took.</summary>
/// <param name="Calls">The completed calls.</param>
/// <param name="Elapsed">How long the phase took, until its last call completed.</param>
public sealed record ThroughputPhase(long Calls, TimeSpan Elapsed)
{
    /// <summary>Gets the completed calls per second.</summary>
    public double PerSecond => Calls / Elapsed.TotalSeconds;
}

/// <summary>Calls one client from a fixed number of concurrent workers for a fixed time.</summary>
public static class ThroughputRunner
{
    /// <summary>
    /// Runs one phase: each worker starts a new call until <paramref name="duration"/> is up, and a call in flight then
    /// completes and counts, so every request the mock logs during the phase is a counted call.
    /// </summary>
    /// <param name="adapter">The client to call.</param>
    /// <param name="workers">How many workers call at once.</param>
    /// <param name="duration">How long workers keep starting calls.</param>
    /// <returns>The calls made and the time taken.</returns>
    /// <exception cref="InvalidOperationException">A call read an answer other than the recorded one.</exception>
    public static async Task<ThroughputPhase> RunPhaseAsync(IClientAdapter adapter, int workers, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var counter = new CallCounter();
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task>(workers);
        for (var i = 0; i < workers; i++)
        {
            tasks.Add(Task.Run(() => WorkAsync(adapter, counter, stopwatch, duration)));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        stopwatch.Stop();
        return new ThroughputPhase(counter.Value, stopwatch.Elapsed);
    }

    private static async Task WorkAsync(IClientAdapter adapter, CallCounter counter, Stopwatch stopwatch, TimeSpan duration)
    {
        while (stopwatch.Elapsed < duration)
        {
            var outcome = await adapter.CallAsync(CancellationToken.None).ConfigureAwait(false);
            counter.Increment();
            if (!outcome.IsExpected)
            {
                throw new InvalidOperationException(adapter.Client + " read an unexpected answer during the throughput run.");
            }
        }
    }

    private sealed class CallCounter
    {
        private long _value;

        public long Value => Interlocked.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }
}
