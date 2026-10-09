using System.Net;
using BenchmarkDotNet.Attributes;
using Minos.Shared;
using ZeroAlloc.Results;

namespace Minos.Benchmarks;

/// <summary>
/// Each client path while discarding listeners sample every <c>Minos</c> span and enable every instrument, so
/// every tag, deferred read and measurement runs. Compare with <see cref="ClientBenchmarks"/> and
/// <see cref="QuestionSetBenchmarks.EvaluateBuiltSet"/>, which run the same calls with nothing listening; BenchmarkDotNet
/// runs each benchmark in its own process, so these listeners never reach those.
/// </summary>
[MemoryDiagnoser]
public class TelemetryBenchmarks : IDisposable
{
    private const string State = "Help! My payouts have been failing for 3 days.";

    private DiscardingTelemetry _telemetry = null!;
    private HttpClient _evaluateHttp = null!;
    private HttpClient _typedHttp = null!;
    private HttpClient _builtSetHttp = null!;
    private HttpClient _listModelsHttp = null!;
    private HttpClient _typedYieldingHttp = null!;
    private JevClient _evaluateClient = null!;
    private JevClient _typedClient = null!;
    private JevClient _builtSetClient = null!;
    private JevClient _listModelsClient = null!;
    private JevClient _typedYieldingClient = null!;
    private SystemOneRequest _request = null!;
    private JevQuestionSet _triage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _telemetry = new DiscardingTelemetry();
        (_evaluateHttp, _evaluateClient) = ClientBenchmarks.CreateClient(ClientBenchmarks.NoulResponseJson);
        (_typedHttp, _typedClient) = ClientBenchmarks.CreateClient(ClientBenchmarks.TriageResponseJson);
        (_builtSetHttp, _builtSetClient) = ClientBenchmarks.CreateClient(ClientBenchmarks.TriageResponseJson);
        (_listModelsHttp, _listModelsClient) = ClientBenchmarks.CreateClient(ClientBenchmarks.ModelsResponseJson);
        (_typedYieldingHttp, _typedYieldingClient) = ClientBenchmarks.CreateClient(
            new YieldingHandler(HttpStatusCode.OK, ClientBenchmarks.TriageResponseJson), loggerFactory: null);
        _request = new SystemOneRequest
        {
            State = State,
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        };
        _triage = QuestionSetBenchmarks.TriageBuilder().Build().Value;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _evaluateClient.Dispose();
        _typedClient.Dispose();
        _builtSetClient.Dispose();
        _listModelsClient.Dispose();
        _typedYieldingClient.Dispose();
        _evaluateHttp.Dispose();
        _typedHttp.Dispose();
        _builtSetHttp.Dispose();
        _listModelsHttp.Dispose();
        _typedYieldingHttp.Dispose();
        // BenchmarkDotNet may also dispose this instance; both calls are idempotent.
        Dispose();
    }

    /// <summary>Detaches the listeners.</summary>
    public void Dispose()
    {
        // Null-conditional: Dispose can run when Setup never did.
        _telemetry?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary><see cref="ClientBenchmarks.EvaluateAsync"/> while listening.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateListeningAsync() => _evaluateClient.EvaluateAsync(_request);

    /// <summary><see cref="ClientBenchmarks.TypedEvaluateAsync"/> while listening: the deferred reads run.</summary>
    [Benchmark]
    public ValueTask<Result<BenchTriage, JevError>> TypedEvaluateListeningAsync() => _typedClient.EvaluateAsync<BenchTriage>(State);

    /// <summary><see cref="QuestionSetBenchmarks.EvaluateBuiltSet"/>'s three-question triage set while listening.</summary>
    [Benchmark]
    public ValueTask<Result<JevAnswers, JevError>> EvaluateBuiltSetListeningAsync() => _builtSetClient.EvaluateAsync(_triage, State);

    /// <summary><see cref="ClientBenchmarks.ListModelsAsync"/> while listening.</summary>
    [Benchmark]
    public ValueTask<Result<ModelList, JevError>> ListModelsListeningAsync() => _listModelsClient.ListModelsAsync();

    /// <summary><see cref="ClientBenchmarks.TypedEvaluateYieldingAsync"/> while listening: the proxy's own state machine runs too.</summary>
    [Benchmark]
    public ValueTask<Result<BenchTriage, JevError>> TypedEvaluateYieldingListeningAsync() => _typedYieldingClient.EvaluateAsync<BenchTriage>(State);
}
