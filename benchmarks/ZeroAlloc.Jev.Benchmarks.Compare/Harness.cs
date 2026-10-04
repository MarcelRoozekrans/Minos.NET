using System.Globalization;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>
/// Runs the comparison: per client, an exact-count probe and a counted throughput run, then BenchmarkDotNet for
/// latency and allocations, and writes the shared result file.
/// </summary>
/// <param name="options">The command line.</param>
/// <param name="log">Where progress goes.</param>
public sealed class Harness(CompareOptions options, TextWriter log)
{
    /// <summary>How many workers call at once in the throughput run.</summary>
    public const int Concurrency = 16;

    /// <summary>How many sequential calls the exact-count probe makes per client.</summary>
    public const int ProbeCalls = 100;

    private TimeSpan ThroughputWarmup => options.Smoke ? TimeSpan.FromSeconds(0.5) : TimeSpan.FromSeconds(2);

    private TimeSpan ThroughputDuration => options.Smoke ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10);

    /// <summary>Runs every measurement and writes the result file.</summary>
    /// <param name="cancellationToken">Cancels the run between measurements.</param>
    /// <returns>The result file's path.</returns>
    /// <exception cref="InvalidOperationException">A request count, an answer or a benchmark failed its check.</exception>
    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutDirectory);
        var throughput = new Dictionary<string, double>(StringComparer.Ordinal);
        var clients = new Dictionary<string, IClientAdapter>(StringComparer.Ordinal);
        using var requestLog = new MockRequestLog(options.BaseUrl);
        try
        {
            foreach (var client in ClientAdapters.All)
            {
                var adapter = ClientAdapters.Create(client, options.BaseUrl);
                clients[client] = adapter;
                await WarmUpAsync(adapter, cancellationToken).ConfigureAwait(false);
                await ProbeAsync(adapter, requestLog, cancellationToken).ConfigureAwait(false);
                throughput[client] = await MeasureThroughputAsync(adapter, requestLog, cancellationToken).ConfigureAwait(false);
            }

            var summary = RunBenchmarks();
            var file = new ResultFile(Machine(summary), Results(summary, clients, throughput));
            var path = Path.Combine(options.OutDirectory, options.ResultFileName);
            await File.WriteAllTextAsync(path, file.ToJson(), cancellationToken).ConfigureAwait(false);
            return path;
        }
        finally
        {
            foreach (var adapter in clients.Values)
            {
                adapter.Dispose();
            }
        }
    }

    private static async Task WarmUpAsync(IClientAdapter adapter, CancellationToken cancellationToken)
    {
        for (var i = 0; i < CompareBenchmarks.WarmupCalls; i++)
        {
            var answers = await adapter.AskAsync(cancellationToken).ConfigureAwait(false);
            answers.EnsureEquals(adapter.Client, Workload.Expected);
        }
    }

    // Sequential calls: one request per call, with no retry and no extra request.
    private static Task<int> ProbeAsync(IClientAdapter adapter, MockRequestLog requestLog, CancellationToken cancellationToken)
        => CountedAsync(adapter.Client, "probe", requestLog, async () =>
        {
            for (var i = 0; i < ProbeCalls; i++)
            {
                var outcome = await adapter.CallAsync(cancellationToken).ConfigureAwait(false);
                if (!outcome.IsExpected)
                {
                    throw new InvalidOperationException(adapter.Client + " read an unexpected answer in the probe.");
                }
            }

            return ProbeCalls;
        }, static calls => calls, cancellationToken);

    // The warm-up and the measured phase are counted apart, each from an empty log, so neither fills the capped log.
    private async Task<double> MeasureThroughputAsync(IClientAdapter adapter, MockRequestLog requestLog, CancellationToken cancellationToken)
    {
        var warmup = await CountedPhaseAsync(adapter, "throughput warm-up", ThroughputWarmup, requestLog, cancellationToken).ConfigureAwait(false);
        var measured = await CountedPhaseAsync(adapter, "throughput run", ThroughputDuration, requestLog, cancellationToken).ConfigureAwait(false);
        await log.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{adapter.Client}: probe {ProbeCalls} of {ProbeCalls} requests, warm-up {warmup.Calls} of {warmup.Calls}, measured {measured.Calls} of {measured.Calls}, {measured.PerSecond:F0}/s")).ConfigureAwait(false);
        return measured.PerSecond;
    }

    private static Task<ThroughputPhase> CountedPhaseAsync(
        IClientAdapter adapter, string run, TimeSpan duration, MockRequestLog requestLog, CancellationToken cancellationToken)
        => CountedAsync(
            adapter.Client,
            run,
            requestLog,
            () => ThroughputRunner.RunPhaseAsync(adapter, Concurrency, duration),
            static phase => phase.Calls,
            cancellationToken);

    // Reads the mock's request count before and after the calls, from an empty log, and fails unless it rose by
    // exactly the calls made.
    private static async Task<T> CountedAsync<T>(
        string client, string run, MockRequestLog requestLog, Func<Task<T>> makeCalls, Func<T, long> callsMade, CancellationToken cancellationToken)
    {
        await requestLog.ClearAsync(cancellationToken).ConfigureAwait(false);
        var before = await requestLog.CountAsync(cancellationToken).ConfigureAwait(false);
        var result = await makeCalls().ConfigureAwait(false);
        var after = await requestLog.CountAsync(cancellationToken).ConfigureAwait(false);
        EnsureCount(client, run, callsMade(result), after - before);
        return result;
    }

    private static void EnsureCount(string client, string run, long calls, long requests)
    {
        if (requests != calls)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{client}: the {run} made {calls} calls but the mock logged {requests} requests. A retry, an extra request or a full request log breaks the one-request-per-call rule."));
        }
    }

    private Summary RunBenchmarks()
    {
        var job = (options.Smoke ? Job.Dry : Job.Default)
            .WithEnvironmentVariables(new EnvironmentVariable(CompareBenchmarks.BaseUrlVariable, options.BaseUrl.AbsoluteUri));
        var config = DefaultConfig.Instance
            .AddJob(job)
            .AddColumn(StatisticColumn.Median, new P99Column())
            .WithArtifactsPath(Path.Combine(options.OutDirectory, "BenchmarkDotNet.Artifacts"));
        var summary = BenchmarkRunner.Run<CompareBenchmarks>(config);
        if (summary.HasCriticalValidationErrors || summary.Reports.Length != CompareBenchmarks.ClientByBenchmark.Count || summary.Reports.Any(r => !r.Success))
        {
            throw new InvalidOperationException("BenchmarkDotNet did not complete every benchmark; see its log in " + summary.LogFilePath);
        }

        return summary;
    }

    private MachineInfo Machine(Summary summary) => new(
        options.Machine,
        RuntimeInformation.OSDescription,
        summary.HostEnvironmentInfo.Cpu.Value?.ProcessorName ?? RuntimeInformation.ProcessArchitecture.ToString(),
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    private static List<ClientResult> Results(Summary summary, Dictionary<string, IClientAdapter> clients, Dictionary<string, double> throughput)
    {
        var results = new List<ClientResult>();
        foreach (var client in ClientAdapters.All)
        {
            var report = summary.Reports.First(r => string.Equals(
                CompareBenchmarks.ClientByBenchmark[r.BenchmarkCase.Descriptor.WorkloadMethod.Name], client, StringComparison.Ordinal));
            var statistics = report.ResultStatistics
                ?? throw new InvalidOperationException(client + ": BenchmarkDotNet reported no statistics.");
            var p99 = P99Column.Nanoseconds(report)
                ?? throw new InvalidOperationException(client + ": BenchmarkDotNet reported no 99th percentile.");
            var adapter = clients[client];
            results.Add(new ClientResult(
                client,
                adapter.Library,
                adapter.Version,
                ".NET",
                Environment.Version.ToString(),
                new LatencyFigures(ToMilliseconds(statistics.Mean), ToMilliseconds(statistics.Median), ToMilliseconds(p99)),
                Math.Round(throughput[client], 1),
                Concurrency,
                report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase))
            {
                Note = adapter.Note,
            });
        }

        return results;
    }

    private static double ToMilliseconds(double nanoseconds) => Math.Round(nanoseconds / 1_000_000, 6);
}
