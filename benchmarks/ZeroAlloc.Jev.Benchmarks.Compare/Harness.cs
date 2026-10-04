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
/// Runs the comparison: per client, a checked warm-up, a timed sequential latency loop and a throughput run, each
/// counted against the mock; then the mock's own ceiling; then BenchmarkDotNet for allocated bytes per call. It writes
/// the shared result file.
/// </summary>
/// <param name="options">The command line.</param>
/// <param name="log">Where progress goes.</param>
public sealed class Harness(CompareOptions options, TextWriter log)
{
    /// <summary>How many workers call at once in the throughput run.</summary>
    public const int Concurrency = 16;

    /// <summary>How many workers the raw client uses to measure the mock's own ceiling.</summary>
    public const int CeilingConcurrency = 64;

    private int LatencyWarmupCalls => options.Smoke ? 10 : 200;

    private int LatencyCalls => options.Smoke ? 20 : 2000;

    private TimeSpan ThroughputWarmup => options.Smoke ? TimeSpan.FromSeconds(0.5) : TimeSpan.FromSeconds(2);

    private TimeSpan ThroughputDuration => options.Smoke ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10);

    /// <summary>Runs every measurement and writes the result file.</summary>
    /// <param name="cancellationToken">Cancels the run between measurements.</param>
    /// <returns>The result file's path.</returns>
    /// <exception cref="InvalidOperationException">A request count, an answer or a benchmark failed its check.</exception>
    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutDirectory);
        var measured = new Dictionary<string, ClientMeasurement>(StringComparer.Ordinal);
        using var counter = new MockRequestCounter(options.BaseUrl);
        foreach (var client in ClientAdapters.All)
        {
            using var adapter = ClientAdapters.Create(client, options.BaseUrl);
            measured[client] = await MeasureAsync(adapter, counter, cancellationToken).ConfigureAwait(false);
        }

        var ceiling = await MeasureCeilingAsync(counter, cancellationToken).ConfigureAwait(false);
        var summary = RunBenchmarks();
        var file = new ResultFile(Machine(summary, ceiling), Results(summary, measured));
        var path = Path.Combine(options.OutDirectory, options.ResultFileName);
        await File.WriteAllTextAsync(path, file.ToJson(), cancellationToken).ConfigureAwait(false);
        return path;
    }

    private async Task<ClientMeasurement> MeasureAsync(IClientAdapter adapter, MockRequestCounter counter, CancellationToken cancellationToken)
    {
        await WarmUpAsync(adapter, counter, cancellationToken).ConfigureAwait(false);

        // Sequential calls, so the count also proves one request per call, with no retry and no extra request.
        var latency = await CountedAsync(
            adapter.Client,
            "latency loop",
            counter,
            () => LatencyRunner.RunAsync(adapter, LatencyWarmupCalls, LatencyCalls, cancellationToken),
            static run => run.Calls,
            cancellationToken).ConfigureAwait(false);

        // The throughput warm-up and the measured phase are counted apart, so a mismatch names the phase.
        var warmup = await CountedPhaseAsync(adapter, "throughput warm-up", Concurrency, ThroughputWarmup, counter, cancellationToken).ConfigureAwait(false);
        var throughput = await CountedPhaseAsync(adapter, "throughput run", Concurrency, ThroughputDuration, counter, cancellationToken).ConfigureAwait(false);

        await log.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{adapter.Client}: latency {latency.Calls} of {latency.Calls} requests, mean {latency.Latency.Mean} ms, p50 {latency.Latency.P50} ms, p99 {latency.Latency.P99} ms; throughput warm-up {warmup.Calls} of {warmup.Calls}, measured {throughput.Calls} of {throughput.Calls}, {throughput.PerSecond:F0}/s")).ConfigureAwait(false);
        return new ClientMeasurement(adapter.Library, adapter.Version, adapter.Note, latency.Latency, throughput.PerSecond);
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

    // The mock's own ceiling: the raw client, the thinnest one, at four times the clients' concurrency, so the results
    // can show how far below the mock's limit each client stays.
    private async Task<double> MeasureCeilingAsync(MockRequestCounter counter, CancellationToken cancellationToken)
    {
        using var raw = ClientAdapters.Create(ClientAdapters.Raw, options.BaseUrl);
        _ = await CountedPhaseAsync(raw, "mock ceiling warm-up", CeilingConcurrency, ThroughputWarmup, counter, cancellationToken).ConfigureAwait(false);
        var measured = await CountedPhaseAsync(raw, "mock ceiling run", CeilingConcurrency, ThroughputDuration, counter, cancellationToken).ConfigureAwait(false);
        await log.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"mock ceiling: {CeilingConcurrency} workers on the raw client, measured {measured.Calls} of {measured.Calls}, {measured.PerSecond:F0}/s")).ConfigureAwait(false);
        return measured.PerSecond;
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
        options.Cores is { } cores ? CoreAffinity.Describe(cores) : null);

    private static List<ClientResult> Results(Summary summary, Dictionary<string, ClientMeasurement> measured)
    {
        var results = new List<ClientResult>();
        foreach (var client in ClientAdapters.All)
        {
            var report = summary.Reports.First(r => string.Equals(
                CompareBenchmarks.ClientByBenchmark[r.BenchmarkCase.Descriptor.WorkloadMethod.Name], client, StringComparison.Ordinal));
            var measurement = measured[client];
            results.Add(new ClientResult(
                client,
                measurement.Library,
                measurement.Version,
                ".NET",
                Environment.Version.ToString(),
                measurement.Latency,
                Math.Round(measurement.ThroughputPerSecond, 1),
                Concurrency,
                report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase))
            {
                Note = measurement.Note,
            });
        }

        return results;
    }

    private sealed record ClientMeasurement(string Library, string Version, string? Note, LatencyFigures Latency, double ThroughputPerSecond);
}
