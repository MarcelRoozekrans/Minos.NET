using System.Text;
using Minos.Recording;
using ZeroAlloc.Results;

namespace Minos.Evaluation.Tests;

public sealed class RecordedExportTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 9, 35, 0, TimeSpan.Zero);

    private static DecisionRequest Request()
    {
        var parsed = DecisionDataset.Parse("""{"id":"seed","state":"The app crashes when I pay.","questions":{"urgent":{"type":"noul","instructions":"Does this convey urgency?"}},"expected":{"urgent":true}}""");
        Assert.True(parsed.IsSuccess);
        var item = Assert.Single(parsed.Value);
        return new DecisionRequest(item.Definition, item.State);
    }

    private static FakeDecisionClient Answering()
        => new((r, _) => ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(r.Definition, [QuestionAnswer.Noul(0.9)], "jev-latest"))));

    private static async IAsyncEnumerable<RecordedDecision> ToAsync(List<RecordedDecision> decisions)
    {
        foreach (var decision in decisions)
        {
            await Task.Yield();
            yield return decision;
        }
    }

    [Fact]
    public async Task RecordedDecisions_ExportLoadAndRun()
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-recorded-export-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var options = new RecordingOptions { Capacity = 16, FlushTimeout = TimeSpan.FromSeconds(10), Redact = static state => RedactionResult.Keep(state) };
            var recorder = new DecisionRecorder(new JsonlDecisionRecordSink(path), options);
            using (var client = Answering().AsBuilder().UseRecording(recorder).Build())
            {
                var request = Request();
                foreach (var id in new[] { "d1", "d2", "d3" })
                {
                    using var scope = DecisionRecording.Begin(decisionId: id);
                    Assert.True((await client.EvaluateAsync(request)).IsSuccess);
                }

                var confirmed = new Dictionary<string, ConfirmedAnswer>(StringComparer.Ordinal) { ["urgent"] = ConfirmedAnswer.ForNoul(true) };
                await recorder.AddOutcomeAsync("d1", DecisionOutcome.Confirmed("reviewer-ui", At, confirmed));
                await recorder.AddOutcomeAsync("d1", DecisionOutcome.Signal("mailer", At, "complained", 1));
                await recorder.AddOutcomeAsync("d2", DecisionOutcome.Confirmed("reviewer-ui", At, confirmed));
                await recorder.EraseDecisionAsync("d2");
                await recorder.AddOutcomeAsync("d3", DecisionOutcome.Signal("mailer", At, "opened"));
                await recorder.FlushAsync();
            }

            await recorder.DisposeAsync();

            var decisions = new List<RecordedDecision>();
            await foreach (var decision in DecisionRecordFile.ReadAsync(path, CancellationToken.None))
            {
                decisions.Add(decision);
            }

            using var dataset = new MemoryStream();
            using var signals = new MemoryStream();
            var summary = await DecisionDatasetExport.WriteAsync(ToAsync(decisions), dataset, signals, CancellationToken.None);

            Assert.Equal(1, summary.ItemsWritten);
            Assert.Equal(1, summary.SkippedWithoutConfirmedOutcome);
            Assert.Equal(1, summary.SignalsWritten);

            var signalLines = Encoding.UTF8.GetString(signals.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var signalLine = Assert.Single(signalLines);
            Assert.Contains("\"d1\"", signalLine, StringComparison.Ordinal);

            var parsed = DecisionDataset.Parse(Encoding.UTF8.GetString(dataset.ToArray()));
            Assert.True(parsed.IsSuccess);
            var item = Assert.Single(parsed.Value);
            Assert.Equal("d1", item.Id);

            var fake = Answering();
            var results = await DatasetRunner.RunAsync(fake, parsed.Value);
            var result = Assert.Single(results.Items);
            Assert.Null(result.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
