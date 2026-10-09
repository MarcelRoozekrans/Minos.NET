using System.Text;
using System.Text.Json;
using Xunit.Abstractions;
using Minos.Serialization;
using Minos.Telemetry;
using Minos.Transport;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>What the instrumented proxy and the unwrap cost with nothing listening.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class OperationsCostTests(ITestOutputHelper output)
{
    // Rule 7: the tightest headroom among the existing AOT gates, TypedEvaluateRoundTripWithDiscardingLogger's 3328 B
    // budget over its 2984 B measurement. The plan probe measured the unwrap at 165 B.
    private const int UnwrapHeadroomBytes = 344;

    // Each figure is the median of five runs. Yielding runs vary in both directions: a runtime thread allocating during the
    // loop adds bytes, and a continuation that finds a pooled buffer still cached saves some. So the difference of two such
    // figures can dip below zero: measuring the proxied call against itself over 300 fresh runs on 2026-10-07 gave -1 to
    // +1 B/call. The 8 B allowed below zero matches the AOT smoke app's disabled-logger check. Both hand-rolled yielding
    // helpers, BytesPerCallAsync here and MeasureYieldingAsync there, and their 8 B tolerances are tracked in #104.
    private const int NoiseBytes = 8;

    private static readonly Uri Endpoint = new("https://api.typesafe.ai/");

    [Fact]
    public void NothingListening_RawCall_AddsNothing()
    {
        var proxy = new DecisionOperationsInstrumented(Completed.Instance);
        var request = JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;

        AllocationGate.AssertBudgetValueTask(0, 1000, () => proxy.EvaluateAsync(request, "typesafe", Endpoint, CancellationToken.None), "ProxyRaw");
    }

    [Fact]
    public void NothingListening_ListModels_AddsNothing()
    {
        var proxy = new DecisionOperationsInstrumented(Completed.Instance);

        AllocationGate.AssertBudgetValueTask(0, 1000, () => proxy.ListModelsAsync("typesafe", Endpoint, CancellationToken.None), "ProxyListModels");
    }

    [Fact]
    public void NothingListening_SynchronousTypedCall_AndItsUnwrap_AddNothing()
    {
        var proxy = new DecisionOperationsInstrumented(Completed.Instance);
        var body = TelemetryBodies.EmptyBody();

        AllocationGate.AssertBudgetValueTask(
            0,
            1000,
            () => Evaluated.Unwrap(proxy.EvaluateTypedAsync<UrgencyCheck>(body, "m", "typesafe", Endpoint, 1, CancellationToken.None)),
            "ProxyTypedUnwrap");
    }

    [Fact]
    public void NothingListening_SynchronousBuiltSetCall_AndItsUnwrap_AddNothing()
    {
        var proxy = new DecisionOperationsInstrumented(Completed.Instance);
        var body = TelemetryBodies.EmptyBody();

        AllocationGate.AssertBudgetValueTask(
            0,
            1000,
            () => Evaluated.Unwrap(proxy.EvaluateBuiltSetAsync(body, Completed.Set, "m", "typesafe", Endpoint, CancellationToken.None)),
            "ProxyBuiltSetUnwrap");
    }

    [Fact]
    public async Task NothingListening_AsynchronousTypedCall_TheUnwrapStaysWithinTheBudgetHeadroom()
    {
        var fake = new FakeOperations(Fixture.Text("response-choice.json")) { Yield = true };
        var proxy = new DecisionOperationsInstrumented(fake);

        async Task ProxiedAsync()
        {
            var result = await proxy.EvaluateTypedAsync<DepartmentRouting>(TelemetryBodies.EmptyBody(), "m", "typesafe", Endpoint, 1, CancellationToken.None);
            result.Value.Dispose();
        }

        async Task UnwrappedAsync()
            => _ = await Evaluated.Unwrap(proxy.EvaluateTypedAsync<DepartmentRouting>(TelemetryBodies.EmptyBody(), "m", "typesafe", Endpoint, 1, CancellationToken.None));

        // Interleaved, so both variants see the same machine load in each run; the median of five runs of each is kept,
        // since noise moves a run in either direction and the median ignores an outlier on either side.
        const int Runs = 5;
        var proxiedRuns = new List<long>(Runs);
        var unwrappedRuns = new List<long>(Runs);
        for (var run = 0; run < Runs; run++)
        {
            proxiedRuns.Add(await BytesPerCallAsync(ProxiedAsync));
            unwrappedRuns.Add(await BytesPerCallAsync(UnwrappedAsync));
        }

        proxiedRuns.Sort();
        unwrappedRuns.Sort();
        var proxied = proxiedRuns[Runs / 2];
        var unwrapped = unwrappedRuns[Runs / 2];
        var unwrap = unwrapped - proxied;
        output.WriteLine($"asynchronous typed call through the proxy: {proxied} B/call; with the unwrap: {unwrapped} B/call; the unwrap: {unwrap} B");
        Assert.InRange(unwrap, -NoiseBytes, UnwrapHeadroomBytes);
    }

    // Bytes per call over 2000 sequential awaited calls, after 200 to warm up. The heap is settled before the warm-up,
    // not after it: a forced gen2 GC lets ArrayPool.Shared trim the arrays the warm-up rented, and the measured calls
    // would then pay to rent them again. The same order fixes ZeroAlloc.TestHelpers' AllocationGate, TestHelpers#62.
    private static async Task<long> BytesPerCallAsync(Func<Task> call)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (var i = 0; i < 200; i++)
        {
            await call();
        }

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < 2000; i++)
        {
            await call();
        }

        return (GC.GetTotalAllocatedBytes(precise: true) - before) / 2000;
    }

    /// <summary>Returns the same completed results every call, so a gate measures the proxy and nothing else.</summary>
    private sealed class Completed : IDecisionOperations
    {
        public static readonly Completed Instance = new();

        public static readonly QuestionSet Set = BuiltSets.UrgencyOnly();

        private static readonly SystemOneResponse Response =
            JsonSerializer.Deserialize(Fixture.Text("response-noul.json"), DecisionJsonContext.Default.SystemOneResponse)!;

        private static readonly ModelList Models = new() { Models = [] };

        // Declared after Set, so Set is initialized first.
        private static readonly Result<Evaluated<Answers>, DecisionError> BuiltSet = Result<Evaluated<Answers>, DecisionError>.Success(
            new Evaluated<Answers>(
                TypedEvaluation.ParseResponse(Encoding.UTF8.GetBytes(Fixture.Text("response-noul.json")), Set.Parser).Value,
                TelemetryBodies.RawJsonOf(Fixture.Text("response-noul.json"))));

        public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(SystemOneRequest request, string provider, Uri endpoint, CancellationToken ct)
            => new(Result<SystemOneResponse, DecisionError>.Success(Response));

        // Each typed method owns and disposes body, as the interface requires. A gate passes the same body every call, and
        // only the first Dispose returns it; the later ones do nothing and allocate nothing.
        public ValueTask<Result<Evaluated<T>, DecisionError>> EvaluateTypedAsync<T>(RawJson body, string model, string provider, Uri endpoint, int questionCount, CancellationToken ct)
            where T : IQuestionSet<T>
        {
            body.Dispose();
            return new(Typed<T>.Result);
        }

        public ValueTask<Result<Evaluated<Answers>, DecisionError>> EvaluateBuiltSetAsync(RawJson body, QuestionSet questionSet, string model, string provider, Uri endpoint, CancellationToken ct)
        {
            body.Dispose();
            return new(BuiltSet);
        }

        public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string provider, Uri endpoint, CancellationToken ct)
            => new(Result<ModelList, DecisionError>.Success(Models));

        // One completed result per set type, built once: the unwrap disposes its response every call, which is harmless
        // after the first, since nothing reads it while nothing listens.
        private static class Typed<T>
            where T : IQuestionSet<T>
        {
            public static readonly Result<Evaluated<T>, DecisionError> Result = Result<Evaluated<T>, DecisionError>.Success(
                new Evaluated<T>(
                    TypedEvaluation.ParseResponse<T>(Encoding.UTF8.GetBytes(Fixture.Text("response-noul.json"))).Value,
                    TelemetryBodies.RawJsonOf(Fixture.Text("response-noul.json"))));
        }
    }
}
