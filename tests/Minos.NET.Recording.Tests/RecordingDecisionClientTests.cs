using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class RecordingDecisionClientTests
{
    public static TheoryData<string> Outcomes => new() { "success", "error", "refusal" };

    private static Result<DecisionResponse, DecisionError> Outcome(string name) => name switch
    {
        "success" => Success(),
        "error" => Error(),
        _ => Refusal(),
    };

    private static async Task<DecisionRecord> RecordOneAsync(ScriptedClient inner, DecisionRequest request, TimeProvider? time = null)
    {
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink, time: time);
        using (factory)
        await using (recorder)
        {
            using var client = inner.AsBuilder().UseRecording(recorder).Build();
            await client.EvaluateAsync(request);
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        return Assert.Single(sink.Inner.Records).Record;
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task ReturnsTheInnerResult_Unchanged(string outcome)
    {
        var expected = Outcome(outcome);
        var inner = new ScriptedClient().Then(expected);
        var (recorder, _, factory) = Recorder(new RecordingSpySink());
        using (factory)
        await using (recorder)
        {
            using var client = inner.AsBuilder().UseRecording(recorder).Build();

            var returned = await client.EvaluateAsync(Request());

            Assert.Equal(expected.IsSuccess, returned.IsSuccess);
            if (expected.IsSuccess)
            {
                Assert.Same(expected.Value, returned.Value);
            }
            else
            {
                Assert.Same(expected.Error, returned.Error);
            }
        }
    }

    [Fact]
    public async Task ASuccess_IsRecorded_WithEveryMember()
    {
        var time = new FakeTimeProvider(TestRecords.T0);
        var response = Answer(isCached: true);
        var inner = new ScriptedClient(Metadata, _ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            return ValueTask.CompletedTask;
        }).Then(Result<DecisionResponse, DecisionError>.Success(response));
        var request = Request() with { Model = "model-requested" };

        var record = await RecordOneAsync(inner, request, time);

        Assert.Equal(7, Guid.Parse(record.DecisionId).Version);
        Assert.Null(record.ErasureKey);
        Assert.Equal(0, record.Attempt);
        Assert.Equal(TestRecords.T0, record.Started);
        Assert.Equal(TimeSpan.FromMilliseconds(250), record.Latency);
        Assert.Equal("openai", record.Provider);
        Assert.Equal("https://api.example.test/v1/", record.Endpoint);
        Assert.Equal("model-default", record.DefaultModel);
        Assert.Equal("model-requested", record.RequestedModel);
        Assert.Equal("model-served", record.ServedModel);
        Assert.Equal("resp-1", record.ResponseId);
        Assert.Equal("Together", record.UpstreamProvider);
        Assert.Equal(new DecisionUsage { InputTokens = 120, OutputTokens = 14, Cost = 0.0012 }, record.Usage);
        Assert.True(record.IsCached);
        Assert.Same(request.Definition, record.Definition);
        Assert.Equal(request.State, record.State);
        Assert.Equal(0, record.ImageCount);
        Assert.Empty(record.ImageHashes);
        Assert.Equal(
            [
                new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.9, 0, response.Answers[0].Confidence, []),
                new RecordedAnswer("topic", QuestionKind.Choice, 1, 0, 0, 0.7, [0.3, 0.7]),
            ],
            record.Answers);
        Assert.Null(record.ErrorKind);
        Assert.Empty(record.RefusedKeys);
    }

    [Fact]
    public async Task WithoutMetadata_TheProviderIsUnknown()
    {
        var record = await RecordOneAsync(new ScriptedClient(), Request());

        Assert.Equal("unknown", record.Provider);
        Assert.Null(record.Endpoint);
        Assert.Null(record.DefaultModel);
        Assert.Null(record.RequestedModel);
    }

    [Fact]
    public async Task AnError_KeepsItsKind_AndNoMessage()
    {
        var inner = new ScriptedClient(Metadata).Then(Error("The upstream said MESSAGE-SENTINEL."));

        var record = await RecordOneAsync(inner, Request());

        Assert.Equal(DecisionErrorKind.Server, record.ErrorKind);
        Assert.Empty(record.Answers);
        Assert.Null(record.Usage);
        Assert.Null(record.ServedModel);
        Assert.Empty(record.RefusedKeys);
        Assert.DoesNotContain("MESSAGE-SENTINEL", TestRecords.Line(record), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusal_KeepsTheRefusedKeys()
    {
        var record = await RecordOneAsync(new ScriptedClient().Then(Refusal()), Request());

        Assert.Equal(DecisionErrorKind.Refused, record.ErrorKind);
        Assert.Equal(["urgent", "topic"], record.RefusedKeys);
        Assert.DoesNotContain("No.", TestRecords.Line(record), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Images_KeepCountAndHashes_NoBytes()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 4, 5];
        var request = Request() with { Images = [DecisionImage.FromBytes(png, "image/png"), DecisionImage.FromBytes(jpeg, "image/jpeg")] };

        var record = await RecordOneAsync(new ScriptedClient(), request);

        Assert.Equal(2, record.ImageCount);
        Assert.Equal([Convert.ToHexStringLower(SHA256.HashData(png)), Convert.ToHexStringLower(SHA256.HashData(jpeg))], record.ImageHashes);
        var line = TestRecords.Line(record);
        Assert.DoesNotContain(Convert.ToBase64String(png), line, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlockingSink_DoesNotDelayTheDecision()
    {
        var sink = new BlockingSink();
        var (recorder, _, factory) = Recorder(sink, new RecordingOptions { FlushTimeout = TimeSpan.FromMilliseconds(50) });
        try
        {
            using (factory)
            await using (recorder)
            {
                using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();

                var first = await client.EvaluateAsync(Request()).AsTask().WaitAsync(Bound);
                await sink.WaitEnteredAsync();
                var second = await client.EvaluateAsync(Request()).AsTask().WaitAsync(Bound);

                // The sink holds the first record, and the second waits behind it: both calls returned anyway.
                Assert.True(first.IsSuccess);
                Assert.True(second.IsSuccess);
                Assert.Empty(sink.Spy.Calls);
            }
        }
        finally
        {
            // The dispose gave up on the sink and cancelled its call: wait for the writer, so nothing counts into the next test.
            await recorder.Writer.WaitAsync(Bound);
        }
    }

    [Fact]
    public async Task AThrowingSink_NeverFailsTheDecision()
    {
        using var metrics = new MetricCapture();
        var sink = new ThrowingSink(_ => true);
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            using var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();

            var result = await client.EvaluateAsync(Request());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);

            Assert.True(result.IsSuccess);
            Assert.Single(sink.Thrown);
            Assert.Equal(1, metrics.Sum("minos.recording.records", "sink_failed"));
        }
    }
}
