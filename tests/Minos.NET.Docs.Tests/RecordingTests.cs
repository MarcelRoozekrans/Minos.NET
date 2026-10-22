using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Minos.Recording;
using ZeroAlloc.Results;

namespace Minos.Docs.Tests;

/// <summary>The recording page's examples run, and do what the page says they do.</summary>
public sealed class RecordingTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("minos-docs-recording-");

    private string RecordFile => Path.Combine(_directory.FullName, "decisions.jsonl");

    [Fact]
    public async Task TheApp_RecordsOneDecision_WithItsScopeAndOutcomes()
    {
        string? decisionId = null;
        SupportLane? queue = null;
        using var services = CacheServices();

        await RecordedApp.RunAsync(new TriageClient(), RecordFile, services, async client =>
        {
            (queue, decisionId) = await TicketDecisions.RouteAsync(client, "customer-7", "The app crashes when I pay.", CancellationToken.None);
            var recorder = client.GetService<IDecisionRecorder>()!;
            await TicketOutcomes.ConfirmAsync(recorder, decisionId!, CancellationToken.None);
            await TicketOutcomes.ComplainedAsync(recorder, decisionId!, CancellationToken.None);
        });

        Assert.Equal(SupportLane.Technical, queue);

        // The question set is written in the shape the page's example shows, with the option keys the outcome confirms.
        Assert.Contains(
            "\"questions\":{\"is_urgent\":{\"type\":\"noul\",\"instructions\":\"Does this convey urgency?\"},\"queue\":{\"type\":\"choice\",\"instructions\":\"Which queue should handle this?\",\"criteria\":{\"billing\":\"Payments, invoices and refunds\",\"technical\":\"Bugs, outages and integrations\"}}}",
            await File.ReadAllTextAsync(RecordFile),
            StringComparison.Ordinal);
        Assert.Equal(7, Guid.Parse(decisionId!).Version);
        var decision = Assert.Single(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.Equal(decisionId, decision.Record.DecisionId);
        Assert.Equal("customer-7", decision.Record.ErasureKey);
        Assert.Equal(["is_urgent", "queue"], decision.Record.Answers.Select(answer => answer.Key));
        var confirmed = Assert.IsType<ConfirmedOutcome>(decision.Outcomes[0]);
        Assert.Equal(ConfirmedAnswer.ForChoice("technical"), confirmed.Answers["queue"]);
        Assert.Equal(ConfirmedAnswer.ForNoul(true), confirmed.Answers["is_urgent"]);
        var signal = Assert.IsType<SignalOutcome>(decision.Outcomes[1]);
        Assert.Equal(("complained", 1d), (signal.Name, signal.Value));
    }

    [Fact]
    public async Task AnErasure_RemovesTheCustomersRecords_AndCompactionLeavesAnEmptyFile()
    {
        bool erased = false;
        using var services = CacheServices();

        await RecordedApp.RunAsync(new TriageClient(), RecordFile, services, async client =>
        {
            _ = await TicketDecisions.RouteAsync(client, "customer-7", "The app crashes when I pay.", CancellationToken.None);
            erased = await CustomerErasure.ForgetAsync(client.GetService<IDecisionRecorder>()!, "customer-7", CancellationToken.None);
        });

        Assert.True(erased);
        Assert.Equal(2, File.ReadAllLines(RecordFile).Length);
        Assert.Empty(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.Equal(0, await RecordFileReading.CompactAsync(RecordFile, CancellationToken.None));
        Assert.Empty(File.ReadAllLines(RecordFile));
    }

    [Fact]
    public async Task AnErasureTheSinkCannotRun_IsReportedAsNotConfirmed()
    {
        await using var recorder = new DecisionRecorder(new FailingSink());

        Assert.False(await CustomerErasure.ForgetAsync(recorder, "customer-7", CancellationToken.None));
    }

    [Fact]
    public async Task AnErasureOnADisposedRecorder_IsReportedAsNotConfirmed()
    {
        var recorder = new DecisionRecorder(new InMemoryDecisionRecordSink());
        await recorder.DisposeAsync();

        Assert.False(await CustomerErasure.ForgetAsync(recorder, "customer-7", CancellationToken.None));
    }

    [Fact]
    public async Task TheLineStoreSink_DeletesOnErasure_AndExportsAFileTheReaderReads()
    {
        var sink = new LineStoreSink();
        await using (var recorder = new DecisionRecorder(sink))
        {
            using var client = new TriageClient().AsBuilder().UseRecording(recorder).Build();
            var (_, kept) = await TicketDecisions.RouteAsync(client, "customer-7", "The app crashes when I pay.", CancellationToken.None);
            _ = await TicketDecisions.RouteAsync(client, "customer-8", "Refund my invoice.", CancellationToken.None);
            await TicketOutcomes.ComplainedAsync(recorder, kept!, CancellationToken.None);
            Assert.True(await CustomerErasure.ForgetAsync(recorder, "customer-8", CancellationToken.None));
        }

        await sink.ExportAsync(RecordFile, CancellationToken.None);

        // The erased customer's record is gone from the store itself, not only from what a read keeps.
        Assert.DoesNotContain("customer-8", await File.ReadAllTextAsync(RecordFile), StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\":\"erasure\"", await File.ReadAllTextAsync(RecordFile), StringComparison.Ordinal);
        var decision = Assert.Single(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.Equal("customer-7", decision.Record.ErasureKey);
        Assert.IsType<SignalOutcome>(Assert.Single(decision.Outcomes));
    }

    [Fact]
    public async Task TheRedactor_MasksAddressesInText_AndKeepsNoStructuredState()
    {
        var sink = new InMemoryDecisionRecordSink();
        await using (var recorder = new DecisionRecorder(sink, StateRedaction.Options()))
        {
            using var client = new TriageClient().AsBuilder().UseRecording(recorder).Build();
            await client.EvaluateAsync<SupportTriage>("Mail me at ann@example.com please.", CancellationToken.None);
            using var json = JsonDocument.Parse("""{"email":"ann@example.com"}""");
            await client.EvaluateAsync<SupportTriage>(json.RootElement, CancellationToken.None);
            await recorder.FlushAsync(CancellationToken.None);
        }

        var record = Assert.Single(sink.Records).Record;
        Assert.True(record.State.TryGetString(out var text));
        Assert.Equal("Mail me at [email] please.", text);
    }

    [Fact]
    public async Task TheContainer_SharesOneRecorder_WithTheRegisteredClient()
    {
        var services = new ServiceCollection().AddRecordedDecisions("docs-key", RecordFile);
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.Same(provider.GetRequiredService<DecisionRecorder>(), client.GetService<IDecisionRecorder>());
        Assert.Same(provider.GetRequiredService<DecisionRecorder>(), provider.GetRequiredService<IDecisionRecorder>());
    }

    [Fact]
    public async Task TheInMemorySink_HoldsTheRecordAfterAFlush()
    {
        var decision = await RecordingInTests.RecordOneAsync(new TriageClient(), CancellationToken.None);

        Assert.Equal(2, decision.Record.Definition.Questions.Count);
        Assert.Empty(decision.Outcomes);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private static ServiceProvider CacheServices()
        => new ServiceCollection().AddMemoryCache().AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance).BuildServiceProvider();

    // Answers every SupportTriage request: urgent, and the Technical queue.
    private sealed class TriageClient : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
                request.Definition,
                [QuestionAnswer.Noul(0.9), QuestionAnswer.Choice(1, 0.8, [0.2, 0.8])],
                "jev-1.14",
                new DecisionUsage { InputTokens = 100, OutputTokens = 2 })));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FailingSink : IDecisionRecordSink
    {
        public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken) => throw new IOException("disk full");

        public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken) => throw new IOException("disk full");

        public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken) => throw new IOException("disk full");

        public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken) => throw new IOException("disk full");
    }
}
