using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class ScopeTests
{
    private static async Task<IReadOnlyList<DecisionRecord>> RecordAsync(ScriptedClient inner, Func<IDecisionClient, Task> calls)
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
}
