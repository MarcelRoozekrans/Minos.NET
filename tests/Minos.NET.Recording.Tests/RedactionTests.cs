using Microsoft.Extensions.Logging;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class RedactionTests
{
    [Fact]
    public async Task ARewrittenState_IsWhatTheSinkSees()
    {
        var inner = new ScriptedClient();
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink, new RecordingOptions
        {
            Redact = state => RedactionResult.Keep(state.ToString().Replace("card", "[item]", StringComparison.Ordinal)),
        });
        using (factory)
        await using (recorder)
        {
            using var client = inner.AsBuilder().UseRecording(recorder).Build();
            await client.EvaluateAsync(Request("My card has not arrived."));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal("My [item] has not arrived.", Assert.Single(sink.Inner.Records).Record.State.ToString());

        // The provider saw the state as the app gave it.
        Assert.Equal("My card has not arrived.", Assert.Single(inner.Requests).State.ToString());
    }

    [Fact]
    public async Task Drop_KeepsNoRecord_AndCounts()
    {
        using var metrics = new MetricCapture();
        var sink = new RecordingSpySink();
        var (recorder, logs, factory) = Recorder(sink, new RecordingOptions { Redact = _ => RedactionResult.Drop });
        using (factory)
        await using (recorder)
        {
            using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            var result = await client.EvaluateAsync(Request());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);

            Assert.True(result.IsSuccess);
        }

        Assert.Empty(sink.Calls);
        Assert.Equal(1, metrics.Sum("minos.recording.records", "dropped"));
        Assert.Equal(1, metrics.Sum("minos.recording.records"));

        // A drop is the app's choice, not a failure: nothing is logged.
        Assert.Empty(logs.GetSnapshot());
    }

    public static TheoryData<string> Failures => new() { "null", "uninitialized" };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task ANullOrInvalidResult_IsAFailedRedaction(string failure)
    {
        using var metrics = new MetricCapture();
        var sink = new RecordingSpySink();
        Func<DecisionContent, RedactionResult> redact = failure switch
        {
            "null" => _ => null!,
            _ => _ => RedactionResult.Keep(default),
        };
        var (recorder, logs, factory) = Recorder(sink, new RecordingOptions { Redact = redact });
        using (factory)
        await using (recorder)
        {
            using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            var result = await client.EvaluateAsync(Request());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);

            Assert.True(result.IsSuccess);
        }

        Assert.Empty(sink.Calls);
        Assert.Equal(1, metrics.Sum("minos.recording.records", "failed"));
        Assert.Equal(1, metrics.Sum("minos.recording.records"));
        var failed = Assert.Single(logs.GetSnapshot());
        Assert.Equal(1011, failed.Id.Id);
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Null(failed.Exception);
        var expectedType = string.Equals(failure, "null", StringComparison.Ordinal) ? nameof(InvalidOperationException) : nameof(ArgumentException);
        Assert.Equal(expectedType, Field(failed, "ExceptionType"));
        Assert.Equal($"Minos recording redaction failed with {expectedType}; no record was kept.", failed.Message);
    }

    [Fact]
    public async Task AThrowingRedactor_KeepsNoRecord_AndTheDecisionSucceeds()
    {
        var sink = new RecordingSpySink();
        var (recorder, logs, factory) = Recorder(sink, new RecordingOptions
        {
            Redact = _ => throw new FormatException("The redactor choked on SENTINEL-7f3a."),
        });
        using (factory)
        await using (recorder)
        {
            using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            var result = await client.EvaluateAsync(Request());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);

            Assert.True(result.IsSuccess);
        }

        Assert.Empty(sink.Calls);
        var failed = Assert.Single(logs.GetSnapshot(), record => record.Id.Id == 1011);
        Assert.Equal(nameof(FormatException), Field(failed, "ExceptionType"));
        Assert.DoesNotContain("SENTINEL", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AThrowingLogger_DoesNotFailTheDecision_AndIsCounted()
    {
        using var metrics = new MetricCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(new ThrowingLoggerProvider()));
        await using var recorder = new DecisionRecorder(
            new RecordingSpySink(), new RecordingOptions { Redact = _ => throw new FormatException() }, factory);
        using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, metrics.Sum("minos.recording.log_failures"));
    }

    [Fact]
    public void Keep_RejectsAnUninitializedState()
        => Assert.Throws<ArgumentException>(() => RedactionResult.Keep(default));
}
