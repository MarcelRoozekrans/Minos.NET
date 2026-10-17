using System.Text.Json;
using Minos.Evaluation;
using ZeroAlloc.Results;

namespace Minos.Docs.Tests;

/// <summary>The claims the datasets page makes about running a dataset and reading its report.</summary>
public sealed class DatasetRunTests
{
    private const string Items =
        """
        {"id":"a","state":"one","questions":{"urgent":{"type":"noul","instructions":"Is it urgent?"}},"expected":{"urgent":true}}
        {"id":"b","state":"two","questions":{"urgent":{"type":"noul","instructions":"Is it urgent?"}},"expected":{"urgent":false}}
        """;

    [Fact]
    public async Task TheExample_RunsSavesAndReports()
    {
        var directory = Path.Combine(Path.GetTempPath(), "minos-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = directory;
            var datasetPath = Path.Combine(directory, "items.jsonl");
            var resultsPath = Path.Combine(directory, "results.jsonl");
            await File.WriteAllTextAsync(datasetPath, Items);

            var report = await DatasetRun.RunAndReportAsync(new AnsweringClient(), datasetPath, resultsPath);

            var loaded = EvaluationResults.Load(resultsPath);
            Assert.True(loaded.IsSuccess);
            Assert.Equal(2, loaded.Value.Items.Count);
            Assert.Equal(0.5, report.Noul!.Accuracy);
            Assert.Equal(2, report.Latency.Requests);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "report.json")));
            Assert.Equal("minos-report", document.RootElement.GetProperty("kind").GetString());
            Assert.StartsWith("# Calibration report", await File.ReadAllTextAsync(Path.Combine(directory, "report.md")), StringComparison.Ordinal);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class AnsweringClient : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
                request.Definition,
                [QuestionAnswer.Noul(0.9)],
                "jev-1.14",
                new DecisionUsage { InputTokens = 1000, OutputTokens = 0 })));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
