using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

/// <summary>Once the inner client has decided, nothing the recording does can fail the call.</summary>
[Collection(RecordingListeners.Name)]
public sealed class RecordFailureTests
{
    private const string Records = "minos.recording.records";
    private const string LogFailures = "minos.recording.log_failures";
    private const string RecordFailures = "minos.recording.record_failures";

    public static TheoryData<string, string, bool> Paths => new()
    {
        { "drop", Records, false },
        { "drop", Records, true },
        { "full queue", Records, false },
        { "full queue", Records, true },
        { "disposed recorder", Records, false },
        { "disposed recorder", Records, true },
        { "throwing redactor and logger", Records, false },
        { "throwing redactor and logger", LogFailures, false },
        { "throwing redactor and logger", LogFailures, true },
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task AThrowingListener_NeverFailsTheDecision(string path, string brokenInstrument, bool asynchronous)
    {
        using var metrics = new MetricCapture();
        var failuresBefore = metrics.Observe(RecordFailures);
        var logs = new Microsoft.Extensions.Logging.Testing.FakeLogCollector();
        using var factory = string.Equals(path, "throwing redactor and logger", StringComparison.Ordinal)
            ? LoggerFactory.Create(builder => builder.AddProvider(new ThrowingLoggerProvider()))
            : LoggerFactory.Create(builder => builder.AddProvider(new Microsoft.Extensions.Logging.Testing.FakeLoggerProvider(logs)));
        var sink = new BlockingSink();
        var options = path switch
        {
            "drop" => new RecordingOptions { Redact = _ => RedactionResult.Drop },
            "full queue" => new RecordingOptions { Capacity = 1 },
            "throwing redactor and logger" => new RecordingOptions { Redact = _ => throw new FormatException() },
            _ => new RecordingOptions(),
        };
        var recorder = new DecisionRecorder(sink, options, factory);
        try
        {
            var inner = asynchronous ? new ScriptedClient(during: async _ => await Task.Yield()) : new ScriptedClient();
            using var client = inner.AsBuilder().UseRecording(recorder).Build();
            if (string.Equals(path, "disposed recorder", StringComparison.Ordinal))
            {
                await recorder.DisposeAsync();
            }

            if (string.Equals(path, "full queue", StringComparison.Ordinal))
            {
                // One record in the sink and one queued fill the queue, so the next is dropped.
                Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
                await sink.WaitEnteredAsync();
                Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
            }

            using (new ThrowingMeterListener(brokenInstrument))
            {
                var pending = client.EvaluateAsync(Request());
                Assert.Equal(!asynchronous, pending.IsCompleted);
                var result = await pending;

                Assert.True(result.IsSuccess);
            }
        }
        finally
        {
            sink.Release();
            await recorder.DisposeAsync();
        }

        var counted = string.Equals(brokenInstrument, Records, StringComparison.Ordinal) ? 1 : 0;
        Assert.Equal(counted, metrics.Observe(RecordFailures) - failuresBefore);
        if (counted == 1 && !string.Equals(path, "throwing redactor and logger", StringComparison.Ordinal))
        {
            var failed = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1013);
            Assert.Equal(LogLevel.Warning, failed.Level);
            Assert.Equal(nameof(InvalidCastException), Field(failed, "ExceptionType"));
            Assert.Null(failed.Exception);
        }
    }

    [Fact]
    public async Task ACacheHit_WithAThrowingListener_Succeeds_Synchronously()
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

            using (new ThrowingMeterListener(Records))
            {
                var hit = client.EvaluateAsync(Request());

                Assert.True(hit.IsCompletedSuccessfully);
                Assert.True((await hit).Value.IsCached);
            }
        }
    }

    [Fact]
    public async Task LogFailures_IsAnObservableCounter_ThatCountsALoggerThatThrows()
    {
        using var metrics = new MetricCapture();
        var before = metrics.Observe(LogFailures);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new ThrowingLoggerProvider()));
        await using (var recorder = new DecisionRecorder(new RecordingSpySink(), new RecordingOptions { Redact = _ => throw new FormatException() }, factory))
        {
            using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
        }

        Assert.Equal(1, metrics.Observe(LogFailures) - before);
    }

    [Fact]
    public async Task TheCallersCancellation_StillPropagates()
    {
        var (recorder, _, factory) = Recorder(new RecordingSpySink());
        using (factory)
        await using (recorder)
        {
            using var client = new ThrowingClient(new OperationCanceledException()).AsBuilder().UseRecording(recorder).Build();
            using var broken = new ThrowingMeterListener(Records);

            await Assert.ThrowsAsync<OperationCanceledException>(() => client.EvaluateAsync(Request()).AsTask());
        }
    }

    /// <summary>An inner client that throws from EvaluateAsync itself, before returning a task.</summary>
    internal sealed class ThrowingClient(Exception exception) : IDecisionClient
    {
        public int Calls { get; private set; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw exception;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
