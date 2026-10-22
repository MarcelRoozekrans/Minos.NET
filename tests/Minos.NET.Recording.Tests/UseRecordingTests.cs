using Microsoft.Extensions.DependencyInjection;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

[Collection(RecordingListeners.Name)]
public sealed class UseRecordingTests
{
    [Fact]
    public async Task ResolvesTheRecorder_FromServices()
    {
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var collection = new ServiceCollection();
            collection.AddSingleton(recorder);
            using var services = collection.BuildServiceProvider();
            using var client = new ScriptedClient().AsBuilder().UseRecording().Build(services);

            await client.EvaluateAsync(Request());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);

            Assert.Same(recorder, client.GetService<IDecisionRecorder>());
        }

        Assert.Single(sink.Inner.Records);
    }

    [Fact]
    public void Build_Throws_WithoutARecorder()
    {
        var builder = new ScriptedClient().AsBuilder().UseRecording();

        var thrown = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("DecisionRecorder", thrown.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => builder.Build(new ServiceCollection().BuildServiceProvider()));
    }

    [Fact]
    public async Task TwoPipelines_ShareOneWriter()
    {
        const int PerPipeline = 25;
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var builder = new ScriptedClient(during: async _ => await Task.Yield()).AsBuilder().UseRecording(recorder);
            using var first = builder.Build();
            using var second = builder.Build();

            await Task.WhenAll(Enumerable.Range(0, PerPipeline * 2).Select(i => Task.Run(async () =>
            {
                var client = i % 2 == 0 ? first : second;
                Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
            })));
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(PerPipeline * 2, sink.Calls.Count);
        Assert.False(sink.Overlapped);
    }

    [Fact]
    public async Task GetService_FindsTheRecorder()
    {
        var (recorder, _, factory) = Recorder(new RecordingSpySink());
        using (factory)
        await using (recorder)
        {
            using var client = new ScriptedClient(Metadata).AsBuilder().UseRecording(recorder).Build();

            Assert.Same(recorder, client.GetService<IDecisionRecorder>());
            Assert.Same(recorder, client.GetService<DecisionRecorder>());
            Assert.IsType<RecordingDecisionClient>(client);
            Assert.Same(client, client.GetService<RecordingDecisionClient>());
            Assert.Null(client.GetService<IDecisionRecorder>("keyed"));

            // Other services pass through to the inner client.
            Assert.Same(Metadata, client.GetService<DecisionClientMetadata>());
        }
    }

    [Fact]
    public async Task DisposingThePipeline_LeavesTheRecorderRunning()
    {
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var client = new ScriptedClient().AsBuilder().UseRecording(recorder).Build();
            await client.EvaluateAsync(Request());
            client.Dispose();

            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            await recorder.AddOutcomeAsync("d-1", TestRecords.Signal());
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        Assert.Equal(2, sink.Calls.Count);
    }

    [Fact]
    public void TheArguments_AreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => RecordingDecisionClientBuilderExtensions.UseRecording(null!));
        Assert.Throws<ArgumentNullException>(() => new RecordingDecisionClient(new ScriptedClient(), null!));
    }
}
