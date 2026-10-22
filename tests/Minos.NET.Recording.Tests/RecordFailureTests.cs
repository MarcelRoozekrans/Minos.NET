using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

/// <summary>Once the inner client has decided, nothing the recording does can fail the call.</summary>
[Collection(RecordingListeners.Name)]
public sealed class RecordFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailureAfterTheCall_LosesTheRecord_AndNeverTheDecision(bool asynchronous)
    {
        using var metrics = new MetricCapture();
        var sink = new RecordingSpySink();

        // The clock breaks when the stage reads it after the call, so making the record throws.
        var (recorder, logs, factory) = Recorder(sink, time: new ClockThatBreaksAfterTheCall());
        using (factory)
        await using (recorder)
        {
            var inner = asynchronous ? new ScriptedClient(during: async _ => await Task.Yield()) : new ScriptedClient();
            using var client = inner.AsBuilder().UseRecording(recorder).Build();

            var pending = client.EvaluateAsync(Request());
            Assert.Equal(!asynchronous, pending.IsCompleted);
            Assert.True((await pending).IsSuccess);
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Empty(sink.Calls);
        Assert.Equal(1, metrics.Sum("minos.recording.records", "failed"));
        var failed = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1013);
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal(nameof(InvalidCastException), Field(failed, "ExceptionType"));
        Assert.Null(failed.Exception);
    }

    [Fact]
    public async Task ACacheHit_CompletesSynchronously()
    {
        var collection = new ServiceCollection();
        collection.AddMemoryCache();
        using var services = collection.BuildServiceProvider();
        var (recorder, _, factory) = Recorder(new RecordingSpySink(), new RecordingOptions { Redact = _ => RedactionResult.Drop });
        using (factory)
        await using (recorder)
        {
            var inner = new ScriptedClient(during: async _ => await Task.Yield());
            using var client = inner.AsBuilder().UseRecording(recorder).UseCaching().Build(services);
            Assert.True((await client.EvaluateAsync(Request())).IsSuccess);

            var hit = client.EvaluateAsync(Request());

            Assert.True(hit.IsCompletedSuccessfully);
            Assert.True((await hit).Value.IsCached);
        }
    }

    [Fact]
    public async Task TheCallersCancellation_StillPropagates()
    {
        var (recorder, _, factory) = Recorder(new RecordingSpySink());
        using (factory)
        await using (recorder)
        {
            using var client = new ThrowingClient(new OperationCanceledException()).AsBuilder().UseRecording(recorder).Build();

            await Assert.ThrowsAsync<OperationCanceledException>(() => client.EvaluateAsync(Request()).AsTask());
        }
    }

    /// <summary>An inner client that throws from EvaluateAsync itself, before returning a task.</summary>
    private sealed class ThrowingClient(Exception exception) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => throw exception;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
