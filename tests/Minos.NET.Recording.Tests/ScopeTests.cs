using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class ScopeTests
{
    private static async Task<IReadOnlyList<DecisionRecord>> RecordAsync(IDecisionClient inner, Func<IDecisionClient, Task> calls)
    {
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            using var client = inner.AsBuilder().UseRecording(recorder).Build();
            await calls(client);
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.False(sink.Overlapped);
        return [.. sink.Inner.Records.Select(decision => decision.Record)];
    }

    [Fact]
    public async Task ASuppliedId_IsOnTheRecord()
    {
        DecisionRecordingScope? seen = null;
        var records = await RecordAsync(new ScriptedClient(), async client =>
        {
            using var scope = DecisionRecording.Begin("order-42", "customer-7");
            seen = scope;
            await client.EvaluateAsync(Request());
        });

        var record = Assert.Single(records);
        Assert.Equal("order-42", record.DecisionId);
        Assert.Equal("customer-7", record.ErasureKey);
        Assert.Equal("order-42", seen!.DecisionId);
    }

    [Fact]
    public async Task AGeneratedId_IsAVersion7Guid_OnScopeAndRecord()
    {
        string? before = "not read", after = null;
        var records = await RecordAsync(new ScriptedClient(), async client =>
        {
            using var scope = DecisionRecording.Begin(erasureKey: "customer-7");
            before = scope.DecisionId;
            await client.EvaluateAsync(Request());
            after = scope.DecisionId;
        });

        var record = Assert.Single(records);
        Assert.Null(before);
        Assert.NotNull(after);
        Assert.Equal(7, Guid.Parse(after).Version);
        Assert.Equal(after, record.DecisionId);
        Assert.Equal("customer-7", record.ErasureKey);
    }

    [Fact]
    public async Task NoScope_StillRecords()
    {
        var records = await RecordAsync(new ScriptedClient(), async client =>
        {
            await client.EvaluateAsync(Request());
            await client.EvaluateAsync(Request());
        });

        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(7, Guid.Parse(record.DecisionId).Version));
        Assert.All(records, record => Assert.Null(record.ErasureKey));
        Assert.NotEqual(records[0].DecisionId, records[1].DecisionId);
    }

    [Fact]
    public async Task ASecondFreshDecision_InOneScope_ThrowsBeforeTheInnerCall()
    {
        var inner = new ScriptedClient();
        InvalidOperationException? thrown = null;
        var records = await RecordAsync(inner, async client =>
        {
            using var scope = DecisionRecording.Begin("order-42");
            await client.EvaluateAsync(Request());
            thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => client.EvaluateAsync(Request()).AsTask());
        });

        Assert.Equal(1, inner.Calls);
        Assert.Single(records);
        Assert.DoesNotContain("order-42", thrown!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AnInnerCallThatThrows_ReleasesTheScope_ForARetry(bool suppliedId, bool synchronousThrow)
    {
        var first = true;
        IDecisionClient Inner() => synchronousThrow
            ? new FlakyClient()
            : new ScriptedClient(during: _ =>
            {
                if (first)
                {
                    first = false;
                    throw new OperationCanceledException();
                }

                return ValueTask.CompletedTask;
            });

        string? afterFailure = "not read", afterSuccess = null;
        var records = await RecordAsync(Inner(), async client =>
        {
            using var scope = DecisionRecording.Begin(suppliedId ? "order-42" : null);
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.EvaluateAsync(Request()).AsTask());
            afterFailure = scope.DecisionId;
            Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
            afterSuccess = scope.DecisionId;
        });

        // The failed call left no generated id behind, and the retry is the scope's one record.
        Assert.Equal(suppliedId ? "order-42" : null, afterFailure);
        var record = Assert.Single(records);
        Assert.Equal(afterSuccess, record.DecisionId);
        Assert.Equal(0, record.Attempt);
    }

    [Fact]
    public async Task ARetryThatThrows_KeepsTheScopesDecision()
    {
        var calls = 0;
        var inner = new ScriptedClient(during: _ => ++calls == 2 ? throw new OperationCanceledException() : ValueTask.CompletedTask);
        var records = await RecordAsync(inner, async client =>
        {
            using var scope = DecisionRecording.Begin("order-42");
            Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.EvaluateAsync(Request() with { RetryAttempt = 1 }).AsTask());

            // The fresh decision was made by the first call: a retry that failed does not release it.
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.EvaluateAsync(Request()).AsTask());
            Assert.Equal("order-42", scope.DecisionId);
        });

        Assert.Single(records);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public void NestedBegin_Throws()
    {
        using (DecisionRecording.Begin("order-42"))
        {
            var thrown = Assert.Throws<InvalidOperationException>(() => DecisionRecording.Begin("order-43"));
            Assert.DoesNotContain("order-4", thrown.Message, StringComparison.Ordinal);
        }

        // Once the scope is disposed, a new one can begin.
        using var next = DecisionRecording.Begin("order-43");
        Assert.Equal("order-43", next.DecisionId);
    }

    [Fact]
    public void Begin_RejectsAnEmptyIdOrKey()
    {
        Assert.Throws<ArgumentException>(() => DecisionRecording.Begin(string.Empty));
        Assert.Throws<ArgumentException>(() => DecisionRecording.Begin(erasureKey: string.Empty));

        // Nothing was left active by the failed calls.
        using var scope = DecisionRecording.Begin();
    }

    [Fact]
    public async Task FiftyParallelScopes_EachGetTheirOwnRecord()
    {
        const int Count = 50;
        var entered = 0;
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Every call waits inside the inner client until all fifty are there, so all fifty scopes are active at once.
        var inner = new ScriptedClient(during: async _ =>
        {
            if (Interlocked.Increment(ref entered) == Count)
            {
                allEntered.SetResult();
            }

            await allEntered.Task.WaitAsync(Bound);
        });

        var records = await RecordAsync(inner, client => Task.WhenAll(Enumerable.Range(0, Count).Select(i => Task.Run(async () =>
        {
            using var scope = DecisionRecording.Begin($"id-{i}");
            await Task.Yield();
            var result = await client.EvaluateAsync(Request($"state-{i}"));
            Assert.True(result.IsSuccess);
            Assert.Equal($"id-{i}", scope.DecisionId);
        }))));

        Assert.Equal(Count, records.Count);
        Assert.All(records, record => Assert.Equal(record.DecisionId.Replace("id-", "state-", StringComparison.Ordinal), record.State.ToString()));
        Assert.Equal(Count, records.Select(record => record.DecisionId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>An inner client whose first call throws from EvaluateAsync itself, before returning a task.</summary>
    private sealed class FlakyClient : IDecisionClient
    {
        private bool _thrown;

        public ValueTask<ZeroAlloc.Results.Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new OperationCanceledException();
            }

            return new(Success());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
