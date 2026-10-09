using BenchmarkDotNet.Attributes;
using Minos.Benchmarks.Compare.Adapters;

namespace Minos.Benchmarks.Compare;

/// <summary>
/// Allocated bytes per call of every client, from BenchmarkDotNet's memory diagnoser; latency comes from the harness's
/// timed loop instead. BenchmarkDotNet runs each benchmark in its own process, which reads the mock's address from
/// <see cref="BaseUrlVariable"/>.
/// </summary>
[MemoryDiagnoser]
public class CompareBenchmarks
{
    /// <summary>The environment variable that carries the mock's address into BenchmarkDotNet's benchmark processes.</summary>
    public const string BaseUrlVariable = "MINOS_COMPARE_BASE_URL";

    /// <summary>How many checked calls each client makes in its global setup, before BenchmarkDotNet measures.</summary>
    public const int WarmupCalls = 3;

    private IClientAdapter _adapter = null!;

    /// <summary>Gets the client each benchmark method measures, by method name.</summary>
    public static IReadOnlyDictionary<string, string> ClientByBenchmark { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(Minos)] = ClientAdapters.Minos,
        [nameof(RawHttpClient)] = ClientAdapters.Raw,
        [nameof(JevSharp)] = ClientAdapters.JevSharp,
        [nameof(TypeSafeSdk)] = ClientAdapters.TypeSafeSdk,
        [nameof(JevNet)] = ClientAdapters.JevNet,
    };

    /// <summary>Creates and warms up the Minos.NET client.</summary>
    /// <returns>A task that completes when the client is warm.</returns>
    [GlobalSetup(Target = nameof(Minos))]
    public Task SetupMinosAsync() => StartAsync(ClientAdapters.Minos);

    /// <summary>Creates and warms up the raw baseline.</summary>
    /// <returns>A task that completes when the client is warm.</returns>
    [GlobalSetup(Target = nameof(RawHttpClient))]
    public Task SetupRawHttpClientAsync() => StartAsync(ClientAdapters.Raw);

    /// <summary>Creates and warms up the JevSharp client.</summary>
    /// <returns>A task that completes when the client is warm.</returns>
    [GlobalSetup(Target = nameof(JevSharp))]
    public Task SetupJevSharpAsync() => StartAsync(ClientAdapters.JevSharp);

    /// <summary>Creates and warms up the TypeSafe.AI.Sdk client.</summary>
    /// <returns>A task that completes when the client is warm.</returns>
    [GlobalSetup(Target = nameof(TypeSafeSdk))]
    public Task SetupTypeSafeSdkAsync() => StartAsync(ClientAdapters.TypeSafeSdk);

    /// <summary>Creates and warms up the Jev.Net client.</summary>
    /// <returns>A task that completes when the client is warm.</returns>
    [GlobalSetup(Target = nameof(JevNet))]
    public Task SetupJevNetAsync() => StartAsync(ClientAdapters.JevNet);

    /// <summary>Disposes the client.</summary>
    [GlobalCleanup]
    public void Cleanup() => _adapter.Dispose();

    /// <summary>One Minos.NET call.</summary>
    /// <returns>What the call read.</returns>
    [Benchmark]
    public ValueTask<CallOutcome> Minos() => _adapter.CallAsync(CancellationToken.None);

    /// <summary>One raw baseline call.</summary>
    /// <returns>What the call read.</returns>
    [Benchmark(Baseline = true)]
    public ValueTask<CallOutcome> RawHttpClient() => _adapter.CallAsync(CancellationToken.None);

    /// <summary>One JevSharp call.</summary>
    /// <returns>What the call read.</returns>
    [Benchmark]
    public ValueTask<CallOutcome> JevSharp() => _adapter.CallAsync(CancellationToken.None);

    /// <summary>One TypeSafe.AI.Sdk call.</summary>
    /// <returns>What the call read.</returns>
    [Benchmark]
    public ValueTask<CallOutcome> TypeSafeSdk() => _adapter.CallAsync(CancellationToken.None);

    /// <summary>One Jev.Net call.</summary>
    /// <returns>What the call read.</returns>
    [Benchmark]
    public ValueTask<CallOutcome> JevNet() => _adapter.CallAsync(CancellationToken.None);

    private async Task StartAsync(string client)
    {
        var baseUrl = Environment.GetEnvironmentVariable(BaseUrlVariable)
            ?? throw new InvalidOperationException(BaseUrlVariable + " is not set; run the benchmarks through the harness.");
        _adapter = ClientAdapters.Create(client, new Uri(baseUrl, UriKind.Absolute));
        for (var i = 0; i < WarmupCalls; i++)
        {
            var answers = await _adapter.AskAsync(CancellationToken.None).ConfigureAwait(false);
            answers.EnsureEquals(client, Workload.Expected);
        }
    }
}
