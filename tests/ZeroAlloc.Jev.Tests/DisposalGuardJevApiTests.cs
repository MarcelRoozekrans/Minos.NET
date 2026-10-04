using System.Text.Json;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Transport;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Jev.Tests;

/// <summary>The decorator that keeps an attempt from being sent once the owning client is disposed.</summary>
public sealed class DisposalGuardJevApiTests
{
    private static readonly SystemOneRequest Request = JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), JevJsonContext.Default.SystemOneRequest)!;

    // Before disposal it hands back the inner call's ValueTask itself, so it adds no allocation and no state machine.
    [Fact]
    public void BeforeDisposal_EveryCall_IsTheInnerCall_AndAllocatesNothing()
    {
        var inner = new CompletedApi();
        var guard = new DisposalGuardJevApi(inner, static () => false);
        using var body = RawJson.Create(System.Buffers.ArrayPool<byte>.Shared, 16);

        AllocationGate.AssertBudgetValueTask(0, 1000, () => guard.EvaluateAsync(Request, "Bearer k", null, CancellationToken.None), "GuardEvaluate");
        AllocationGate.AssertBudgetValueTask(0, 1000, () => guard.EvaluateRawAsync(body, "Bearer k", null, CancellationToken.None), "GuardEvaluateRaw");
        AllocationGate.AssertBudgetValueTask(0, 1000, () => guard.ListModelsAsync("Bearer k", null, CancellationToken.None), "GuardListModels");
        Assert.True(inner.Calls > 0);
    }

    [Fact]
    public void BeforeDisposal_APendingCall_IsReturnedAsIs()
    {
        var pending = new TaskCompletionSource<Result<ModelList, JevError>>();
        var guard = new DisposalGuardJevApi(new PendingApi(pending.Task), static () => false);

        Assert.Same(pending.Task, guard.ListModelsAsync("Bearer k", 1, CancellationToken.None).AsTask());
    }

    [Fact]
    public void AfterDisposal_EveryCall_IsDisposed_WithoutCallingTheInnerApi()
    {
        var inner = new CompletedApi();
        var guard = new DisposalGuardJevApi(inner, static () => true);
        using var body = RawJson.Create(System.Buffers.ArrayPool<byte>.Shared, 16);

        AssertDisposed(guard.EvaluateAsync(Request, "Bearer k", 1, CancellationToken.None));
        AssertDisposed(guard.EvaluateRawAsync(body, "Bearer k", 1, CancellationToken.None));
        AssertDisposed(guard.ListModelsAsync("Bearer k", 1, CancellationToken.None));
        Assert.Equal(0, inner.Calls);
    }

    private static void AssertDisposed<T>(ValueTask<Result<T, JevError>> call)
    {
        Assert.True(call.IsCompletedSuccessfully);
        var error = call.Result.Error;
        Assert.Equal(JevErrorKind.Disposed, error.Kind);
        Assert.Null(error.Exception);
        Assert.False(IJevApi.IsTransient(error));
    }

    // Answers every call synchronously with the same prebuilt result, so the call itself allocates nothing.
    private sealed class CompletedApi : IJevApi
    {
        private static readonly Result<SystemOneResponse, JevError> Evaluated = Result<SystemOneResponse, JevError>.Success(
            JsonSerializer.Deserialize(Fixture.Text("response-noul.json"), JevJsonContext.Default.SystemOneResponse)!);
        private static readonly Result<RawJson, JevError> Raw = Result<RawJson, JevError>.Failure(new JevError(JevErrorKind.Server, "server"));
        private static readonly Result<ModelList, JevError> Models = Result<ModelList, JevError>.Success(new ModelList { Models = [] });

        public int Calls { get; private set; }

        public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
        {
            Calls++;
            return new(Evaluated);
        }

        public ValueTask<Result<RawJson, JevError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
        {
            Calls++;
            return new(Raw);
        }

        public ValueTask<Result<ModelList, JevError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
        {
            Calls++;
            return new(Models);
        }
    }

    private sealed class PendingApi(Task<Result<ModelList, JevError>> pending) : IJevApi
    {
        public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(
            SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<RawJson, JevError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<Result<ModelList, JevError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
            => new(pending);
    }
}
