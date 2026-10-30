using Minos.Evaluation;

namespace Minos.Docs.Tests;

/// <summary>The claims the datasets page makes about exporting recorded decisions.</summary>
public sealed class DatasetExportTests
{
    private const string Records =
        """
        {"kind":"record","version":1,"decisionId":"d1","started":"2026-10-22T09:30:00+00:00","latencyMs":10,"provider":"typesafe","cached":false,"questions":{"is_urgent":{"type":"noul","instructions":"Does this convey urgency?"}},"state":"The app crashes when I pay.","imageCount":0}
        {"kind":"outcome","version":1,"decisionId":"d1","type":"confirmed","source":"reviewer-ui","at":"2026-10-22T09:35:00+00:00","confirmed":{"is_urgent":true}}
        {"kind":"outcome","version":1,"decisionId":"d1","type":"signal","source":"mailer","at":"2026-10-22T10:30:00+00:00","name":"complained","value":1}
        {"kind":"record","version":1,"decisionId":"d2","started":"2026-09-01T09:30:00+00:00","latencyMs":10,"provider":"typesafe","cached":false,"questions":{"is_urgent":{"type":"noul","instructions":"Does this convey urgency?"}},"state":"Old message.","imageCount":0}
        {"kind":"outcome","version":1,"decisionId":"d2","type":"confirmed","source":"reviewer-ui","at":"2026-09-01T09:35:00+00:00","confirmed":{"is_urgent":false}}
        """;

    [Fact]
    public async Task TheExample_ExportsTheChosenDecisions_AndTheDatasetLoads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "minos-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var recordsPath = Path.Combine(directory, "decisions.jsonl");
            var datasetPath = Path.Combine(directory, "dataset.jsonl");
            var signalsPath = Path.Combine(directory, "dataset.signals.jsonl");
            await File.WriteAllTextAsync(recordsPath, Records);

            var summary = await DatasetExport.ExportAsync(recordsPath, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), datasetPath, signalsPath, CancellationToken.None);

            Assert.Equal(1, summary.ItemsWritten);
            Assert.Equal(1, summary.SignalsWritten);
            var loaded = DecisionDataset.Load(datasetPath);
            Assert.True(loaded.IsSuccess, loaded.IsFailure ? string.Join('\n', loaded.Error.Select(problem => problem.ToString())) : string.Empty);
            var item = Assert.Single(loaded.Value);
            Assert.Equal("d1", item.Id);
            Assert.Single(await File.ReadAllLinesAsync(signalsPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
