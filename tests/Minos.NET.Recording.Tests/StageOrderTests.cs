using Microsoft.Extensions.DependencyInjection;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class StageOrderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("minos-stage-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task InsideRetries_ARetriedDecision_KeepsOneId_WithAttempts()
    {
        var path = Path.Combine(_directory, "decisions.jsonl");
        var inner = new ScriptedClient().ThenFail(DecisionErrorKind.Server);
        await using (var sink = new JsonlDecisionRecordSink(path))
        {
            var (recorder, _, factory) = Recorder(sink);
            using (factory)
            await using (recorder)
            {
                // Not the recommended order: the recording stage sits inside the retry stage, so it sees each attempt.
                using var client = inner.AsBuilder()
                    .UseRetries(o =>
                    {
                        o.Jitter = false;
                        o.InitialBackoff = TimeSpan.FromMilliseconds(1);
                        o.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
                    })
                    .UseRecording(recorder)
                    .Build();

                using (DecisionRecording.Begin("order-42", "customer-7"))
                {
                    var result = await client.EvaluateAsync(Request());
                    Assert.True(result.IsSuccess);

                    // A second fresh decision in the scope still fails fast, before the inner call.
                    await Assert.ThrowsAsync<InvalidOperationException>(() => client.EvaluateAsync(Request()).AsTask());
                }

                await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            }
        }

        Assert.Equal(2, inner.Calls);
        Assert.Equal(2, File.ReadAllLines(path).Length);
        var decision = Assert.Single(await DecisionRecordFile.ReadAsync(path, CancellationToken.None).ToListAsync());
        Assert.Equal("order-42", decision.Record.DecisionId);
        Assert.Equal("customer-7", decision.Record.ErasureKey);
        Assert.Equal(1, decision.Record.Attempt);
        Assert.Null(decision.Record.ErrorKind);
    }

    [Fact]
    public async Task OutsideCaching_ACacheHit_IsRecordedAsCached()
    {
        var collection = new ServiceCollection();
        collection.AddMemoryCache();
        using var services = collection.BuildServiceProvider();
        var inner = new ScriptedClient(Metadata);
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            using var client = inner.AsBuilder().UseRecording(recorder).UseCaching().Build(services);

            await client.EvaluateAsync(Request());
            await client.EvaluateAsync(Request());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(1, inner.Calls);
        Assert.Equal([false, true], sink.Inner.Records.Select(decision => decision.Record.IsCached));
        Assert.All(sink.Inner.Records, decision => Assert.Equal(2, decision.Record.Answers.Count));
    }
}
