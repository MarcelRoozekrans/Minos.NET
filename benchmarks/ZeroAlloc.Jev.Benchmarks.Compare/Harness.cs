using System.Globalization;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using ZeroAlloc.Jev.Benchmarks.Compare.Adapters;
using ZeroAlloc.Jev.Benchmarks.Shared;

namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>
/// Runs the comparison: first a warm-up of every client, so none is measured in a cold process; then every client's
/// checked warm-up; then the latency rounds, which interleave the clients; then each client's throughput run, one
/// client at a time, in a rotated order; each counted against the mock. Then the mock ceiling, then BenchmarkDotNet for
/// allocated bytes per call. It writes the shared result file, with the order it used.
/// </summary>
/// <param name="options">The command line.</param>
/// <param name="log">Where progress goes.</param>
public sealed class Harness(CompareOptions options, TextWriter log)
{
    /// <summary>How many workers call at once in the throughput run.</summary>
    public const int Concurrency = 16;

    /// <summary>
    /// The worker counts the raw client tries when it measures the mock ceiling; the ceiling is the best of them.
    /// </summary>
    public static IReadOnlyList<int> CeilingConcurrencies { get; } = [16, 32, 64];

    private int LatencyWarmupCalls => options.Smoke ? 10 : 200;

    // 20 rounds of 100 calls, 2000 timed calls per client; a smoke run times 20, in 4 rounds of 5.
    private int LatencyRounds => options.Smoke ? 4 : 20;

    private int LatencyCallsPerRound => options.Smoke ? 5 : 100;

    private TimeSpan ThroughputWarmup => options.Smoke ? TimeSpan.FromSeconds(0.5) : TimeSpan.FromSeconds(2);

    private TimeSpan ThroughputDuration => options.Smoke ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10);

    /// <summary>Runs every measurement and writes the result file.</summary>
    /// <param name="cancellationToken">Cancels the run between measurements.</param>
    /// <returns>The result file's path.</returns>
    /// <exception cref="InvalidOperationException">A request count, an answer or a benchmark failed its check.</exception>
    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutDirectory);
        using var counter = new MockRequestCounter(options.BaseUrl);
        await WarmUpProcessAsync(counter, cancellationToken).ConfigureAwait(false);

        // A random start unless --rotation names one, so the order differs from run to run; the file records it.
        var rotation = options.Rotation ?? Random.Shared.Next(ClientAdapters.All.Count);
        var (measured, order) = await MeasureClientsAsync(counter, rotation, cancellationToken).ConfigureAwait(false);
        var ceiling = await MeasureCeilingAsync(counter, cancellationToken).ConfigureAwait(false);
        var summary = RunBenchmarks();
        var file = new ResultFile(Machine(summary, ceiling), Results(summary, measured)) { Order = order };
        var path = Path.Combine(options.OutDirectory, options.ResultFileName);
        await File.WriteAllTextAsync(path, file.ToJson(), cancellationToken).ConfigureAwait(false);
        return path;
    }

    // Runs every client's checked warm-up and a throughput warm-up before any client is measured, each on an instance
    // that is then disposed. Without it, the first client measured runs its latency loop while the process is still
    // cold: tiered JIT has not yet recompiled the shared HttpClient, socket and System.Text.Json code, nor the client's
    // own, and the thread pool has not grown. That client then reads as much slower than it is, whichever client it is.
    // The code each client runs is compiled once per process, so the instance measured later starts warm.
    private async Task WarmUpProcessAsync(MockRequestCounter counter, CancellationToken cancellationToken)
    {
        foreach (var client in ClientAdapters.All)
        {
            using var adapter = ClientAdapters.Create(client, options.BaseUrl);
            await WarmUpAsync(adapter, counter, cancellationToken).ConfigureAwait(false);
            _ = await CountedPhaseAsync(adapter, "process warm-up", Concurrency, ThroughputWarmup, counter, cancellationToken).ConfigureAwait(false);
        }
    }

    // Every client lives through the whole measurement, so the latency rounds can interleave them. Each client's
    // throughput window then runs alone, in the rotated order, so no client always runs first or last.
    private async Task<(Dictionary<string, ClientMeasurement> Measured, MeasurementOrder Order)> MeasureClientsAsync(
        MockRequestCounter counter, int rotation, CancellationToken cancellationToken)
    {
        var adapters = new IClientAdapter?[ClientAdapters.All.Count];
        try
        {
            for (var i = 0; i < adapters.Length; i++)
            {
                adapters[i] = ClientAdapters.Create(ClientAdapters.All[i], options.BaseUrl);
            }

            return await MeasureAsync(adapters!, counter, rotation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var adapter in adapters)
            {
                adapter?.Dispose();
            }
        }
    }

    private async Task<(Dictionary<string, ClientMeasurement> Measured, MeasurementOrder Order)> MeasureAsync(
        IClientAdapter[] adapters, MockRequestCounter counter, int rotation, CancellationToken cancellationToken)
    {
        foreach (var adapter in adapters)
        {
            await WarmUpAsync(adapter, counter, cancellationToken).ConfigureAwait(false);
        }

        // Sequential calls, so the count also proves one request per call, with no retry and no extra request.
        var latency = await LatencyRunner.RunRoundsAsync(
            adapters, LatencyWarmupCalls, LatencyRounds, LatencyCallsPerRound, rotation, counter.CountAsync, cancellationToken).ConfigureAwait(false);

        IClientAdapter[] throughputOrder = [.. LatencyRunner.Order(adapters.Length, 0, rotation).Select(i => adapters[i])];
        var measured = new Dictionary<string, ClientMeasurement>(StringComparer.Ordinal);
        foreach (var adapter in throughputOrder)
        {
            // The throughput warm-up and the measured phase are counted apart, so a mismatch names the phase.
            var warmup = await CountedPhaseAsync(adapter, "throughput warm-up", Concurrency, ThroughputWarmup, counter, cancellationToken).ConfigureAwait(false);
            var throughput = await CountedPhaseAsync(adapter, "throughput run", Concurrency, ThroughputDuration, counter, cancellationToken).ConfigureAwait(false);
            var run = latency[adapter.Client];
            await log.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"{adapter.Client}: latency {run.Calls} of {run.Calls} requests, mean {run.Latency.Mean} ms, p50 {run.Latency.P50} ms, p99 {run.Latency.P99} ms; throughput warm-up {warmup.Calls} of {warmup.Calls}, measured {throughput.Calls} of {throughput.Calls}, {throughput.PerSecond:F0}/s")).ConfigureAwait(false);
            measured[adapter.Client] = new ClientMeasurement(adapter.Library, adapter.Version, adapter.Note, run.Latency, throughput.PerSecond);
        }

        var order = new MeasurementOrder(LatencyRounds, LatencyCallsPerRound, rotation, [.. throughputOrder.Select(a => a.Client)]);
        await log.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"order: latency in {order.LatencyRounds} rounds of {order.CallsPerRound} calls per client from rotation {rotation}; throughput {string.Join(", ", order.Throughput)}")).ConfigureAwait(false);
        return (measured, order);
    }

    private static Task<int> WarmUpAsync(IClientAdapter adapter, MockRequestCounter counter, CancellationToken cancellationToken)
        => CountedAsync(adapter.Client, "warm-up", counter, async () =>
        {
            for (var i = 0; i < CompareBenchmarks.WarmupCalls; i++)
            {
                var answers = await adapter.AskAsync(cancellationToken).ConfigureAwait(false);
                answers.EnsureEquals(adapter.Client, Workload.Expected);
            }

            return CompareBenchmarks.WarmupCalls;
        }, static calls => calls, cancellationToken);

    // The mock ceiling: the raw client, the thinnest one, at 16, 32 and 64 workers, each for the throughput duration,
    // keeping the best. One count alone would understate it: 64 workers can cost a few client cores more than they load
    // the mock. It is the best rate the raw client reached, so a lower bound on what the mock can serve; nothing here
    // shows whether the mock or the client side saturated.
    private async Task<double> MeasureCeilingAsync(MockRequestCounter counter, CancellationToken cancellationToken)
    {
        using var raw = ClientAdapters.Create(ClientAdapters.Raw, options.BaseUrl);
        _ = await CountedPhaseAsync(raw, "mock ceiling warm-up", CeilingConcurrencies[^1], ThroughputWarmup, counter, cancellationToken).ConfigureAwait(false);
        var best = await CeilingRunner.RunAsync(CeilingConcurrencies, async workers =>
        {
            var measured = await CountedPhaseAsync(raw, "mock ceiling run", workers, ThroughputDuration, counter, cancellationToken).ConfigureAwait(false);
            await log.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"mock ceiling: {workers} workers on the raw client, measured {measured.Calls} of {measured.Calls}, {measured.PerSecond:F0}/s")).ConfigureAwait(false);
            return measured;
        }).ConfigureAwait(false);
        await log.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"mock ceiling: best {best.PerSecond:F0}/s at {best.Workers} workers")).ConfigureAwait(false);
        return best.PerSecond;
    }

    private static Task<ThroughputPhase> CountedPhaseAsync(
        IClientAdapter adapter, string run, int workers, TimeSpan duration, MockRequestCounter counter, CancellationToken cancellationToken)
        => CountedAsync(
            adapter.Client,
            run,
            counter,
            () => ThroughputRunner.RunPhaseAsync(adapter, workers, duration),
            static phase => phase.Calls,
            cancellationToken);

    // Reads the mock's served-request count before and after the calls, and fails unless it rose by exactly the calls
    // made. The count is never reset, so these windows add up to the run's total.
    private static async Task<T> CountedAsync<T>(
        string client, string run, MockRequestCounter counter, Func<Task<T>> makeCalls, Func<T, long> callsMade, CancellationToken cancellationToken)
    {
        var before = await counter.CountAsync(cancellationToken).ConfigureAwait(false);
        var result = await makeCalls().ConfigureAwait(false);
        var after = await counter.CountAsync(cancellationToken).ConfigureAwait(false);
        EnsureCount(client, run, callsMade(result), after - before);
        return result;
    }

    private static void EnsureCount(string client, string run, long calls, long requests)
    {
        if (requests != calls)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{client}: the {run} made {calls} calls but the mock served {requests} requests. A retry or an extra request breaks the one-request-per-call rule."));
        }
    }

    // BenchmarkDotNet measures allocated bytes per call only; latency comes from the timed loop, as in every harness.
    private Summary RunBenchmarks()
    {
        var job = (options.Smoke ? Job.Dry : Job.ShortRun)
            .WithEnvironmentVariables(new EnvironmentVariable(CompareBenchmarks.BaseUrlVariable, options.BaseUrl.AbsoluteUri));
        if (options.Cores is { } cores)
        {
            // BenchmarkDotNet's benchmark processes run on the harness's cores too.
            job = job.WithAffinity(unchecked((nint)cores));
        }

        var config = DefaultConfig.Instance
            .AddJob(job)
            .WithArtifactsPath(Path.Combine(options.OutDirectory, "BenchmarkDotNet.Artifacts"));
        var summary = BenchmarkRunner.Run<CompareBenchmarks>(config);
        if (summary.HasCriticalValidationErrors || summary.Reports.Length != CompareBenchmarks.ClientByBenchmark.Count || summary.Reports.Any(r => !r.Success))
        {
            throw new InvalidOperationException("BenchmarkDotNet did not complete every benchmark; see its log in " + summary.LogFilePath);
        }

        return summary;
    }

    private MachineInfo Machine(Summary summary, double mockCeilingPerSecond) => new(
        options.Machine,
        RuntimeInformation.OSDescription,
        summary.HostEnvironmentInfo.Cpu.Value?.ProcessorName ?? RuntimeInformation.ProcessArchitecture.ToString(),
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        Math.Round(mockCeilingPerSecond, 1),
        options.Cores is { } cores ? CoreAffinity.Describe(cores) : null,
        options.MockCores is { } mockCores ? CoreAffinity.Describe(mockCores) : null);

    private static List<ClientResult> Results(Summary summary, Dictionary<string, ClientMeasurement> measured)
    {
        var results = new List<ClientResult>();
        foreach (var client in ClientAdapters.All)
        {
            var report = summary.Reports.First(r => string.Equals(
                CompareBenchmarks.ClientByBenchmark[r.BenchmarkCase.Descriptor.WorkloadMethod.Name], client, StringComparison.Ordinal));
            var measurement = measured[client];

            // Bytes are the one figure the .NET clients have and the JS and Python ones don't; a missing one is a
            // failed measurement, not a dash in the table.
            var allocated = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase)
                ?? throw new InvalidOperationException(
                    client + ": BenchmarkDotNet reported no allocated bytes per call; see its log in " + summary.LogFilePath);
            results.Add(new ClientResult(
                client,
                measurement.Library,
                measurement.Version,
                ".NET",
                Environment.Version.ToString(),
                measurement.Latency,
                Math.Round(measurement.ThroughputPerSecond, 1),
                Concurrency,
                allocated)
            {
                Note = measurement.Note,
            });
        }

        return results;
    }

    private sealed record ClientMeasurement(string Library, string Version, string? Note, LatencyFigures Latency, double ThroughputPerSecond);
}
