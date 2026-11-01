using System.Text;
using Minos.Recording;
using Minos.Samples.ShadowReplay;

namespace Minos.Samples.Tests;

/// <summary>The sink the shadow replay waits on: it forwards every call and signals a decision once its shadow part is written.</summary>
public sealed class ShadowSignalSinkTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    // DecisionRecord has no public constructor, so the records come from the file format, as a recorder writes it.
    private static async Task<DecisionRecord> RecordAsync(string id, bool withShadow)
    {
        var shadow = withShadow ? ""","shadow":{"provider":"candidate","latencyMs":40}""" : "";
        var line = $$$"""{"kind":"record","version":1,"decisionId":"{{{id}}}","started":"2026-10-22T09:30:00+00:00","latencyMs":10,"provider":"primary","cached":false,"questions":{"urgent":{"type":"noul","instructions":"Is it urgent?"}},"state":"hello","imageCount":0,"answers":[{"key":"urgent","kind":"noul","probability":0.5,"confidence":0.5}]{{{shadow}}}}""";
        var path = Path.Combine(Path.GetTempPath(), $"shadow-sink-{Guid.NewGuid():N}.jsonl");
        try
        {
            await File.WriteAllTextAsync(path, line + "\n", new UTF8Encoding(false));
            await foreach (var decision in DecisionRecordFile.ReadAsync(path, CancellationToken.None))
            {
                return decision.Record;
            }

            throw new InvalidOperationException("The test record was not read.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Expect_CompletesOnTheShadowWrite()
    {
        var sink = new ShadowSignalSink(new InMemoryDecisionRecordSink());
        var signal = sink.Expect("d-1");

        await sink.WriteAsync(await RecordAsync("d-1", withShadow: false), CancellationToken.None);
        Assert.False(signal.IsCompleted);

        await sink.WriteAsync(await RecordAsync("d-1", withShadow: true), CancellationToken.None);
        await signal.WaitAsync(Bound);
    }

    [Fact]
    public void Expect_ReturnsTheSameTaskForOneId()
    {
        var sink = new ShadowSignalSink(new InMemoryDecisionRecordSink());

        Assert.Same(sink.Expect("d-1"), sink.Expect("d-1"));
    }

    [Fact]
    public async Task Expect_IgnoresOtherDecisions()
    {
        var sink = new ShadowSignalSink(new InMemoryDecisionRecordSink());
        var signal = sink.Expect("d-1");

        await sink.WriteAsync(await RecordAsync("d-2", withShadow: true), CancellationToken.None);

        Assert.False(signal.IsCompleted);
    }

    [Fact]
    public async Task Expect_SignalsOnlyAfterTheInnerWriteReturns()
    {
        var inner = new GatedSink();
        var sink = new ShadowSignalSink(inner);
        var signal = sink.Expect("d-1");

        var write = sink.WriteAsync(await RecordAsync("d-1", withShadow: true), CancellationToken.None).AsTask();
        await inner.Entered.Task.WaitAsync(Bound);
        Assert.False(signal.IsCompleted);

        inner.Release.SetResult();
        await write.WaitAsync(Bound);
        await signal.WaitAsync(Bound);
    }

    [Fact]
    public async Task WriteAsync_ForwardsEveryCall()
    {
        var inner = new InMemoryDecisionRecordSink();
        var sink = new ShadowSignalSink(inner);
        var outcome = DecisionOutcome.Signal("test", DateTimeOffset.UnixEpoch, "clicked", 1);

        await sink.WriteAsync(await RecordAsync("d-1", withShadow: false), CancellationToken.None);
        await sink.AddOutcomeAsync("d-1", outcome, CancellationToken.None);

        var decision = Assert.Single(inner.Records);
        Assert.Equal("d-1", decision.Record.DecisionId);
        Assert.Single(decision.Outcomes);
    }

    [Fact]
    public async Task EraseCalls_AreForwarded()
    {
        var inner = new InMemoryDecisionRecordSink();
        var sink = new ShadowSignalSink(inner);
        await sink.WriteAsync(await RecordAsync("d-1", withShadow: false), CancellationToken.None);
        await sink.WriteAsync(await RecordAsync("d-2", withShadow: false), CancellationToken.None);

        await sink.EraseDecisionAsync("d-1", CancellationToken.None);
        Assert.Equal("d-2", Assert.Single(inner.Records).Record.DecisionId);

        var spy = new EraseSpy();
        await new ShadowSignalSink(spy).EraseAsync("subject-1", CancellationToken.None);
        Assert.Equal("subject-1", spy.ErasedKey);
    }

    [Fact]
    public void Forget_DropsTheRegistration()
    {
        var sink = new ShadowSignalSink(new InMemoryDecisionRecordSink());
        var first = sink.Expect("d-1");

        sink.Forget("d-1");

        Assert.NotSame(first, sink.Expect("d-1"));
    }

    private sealed class GatedSink : IDecisionRecordSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
        {
            Entered.SetResult();
            await Release.Task;
        }

        public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EraseSpy : IDecisionRecordSink
    {
        public string? ErasedKey { get; private set; }

        public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
        {
            ErasedKey = erasureKey;
            return ValueTask.CompletedTask;
        }

        public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
