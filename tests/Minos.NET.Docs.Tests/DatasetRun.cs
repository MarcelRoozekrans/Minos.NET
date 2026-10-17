namespace Minos.Docs.Tests;

#region DatasetRun_RunAndReport
using Minos;
using Minos.Evaluation;

public static class DatasetRun
{
    // Runs every item of a dataset file, keeps the results, and builds the report from them.
    public static async Task<CalibrationReport> RunAndReportAsync(IDecisionClient client, string datasetPath, string resultsPath)
    {
        var loaded = DecisionDataset.Load(datasetPath);
        if (loaded.IsFailure)
        {
            throw new InvalidOperationException("The dataset has problems: " + string.Join("; ", loaded.Error.Select(problem => problem.ToString())));
        }

        var dataset = loaded.Value;

        // One request per item. The default is one at a time; MaxParallelism lets several run together.
        var results = await DatasetRunner.RunAsync(client, dataset, new DatasetRunOptions { DatasetName = "support", MaxParallelism = 4 });

        // The results file holds what the run paid for. Save it before anything else.
        results.Save(resultsPath);

        // The prices are only used for responses that report no cost of their own.
        var price = new ModelPrice
        {
            Model = "jev-1.14",
            InputPerMillion = 0.042,
            OutputPerMillion = 0,
            Date = new DateOnly(2026, 10, 17),
            Source = "OpenRouter listing",
        };
        var prices = new PriceTable([price]);

        // Later, EvaluationResults.Load(resultsPath) brings the same results back, and the report is made again from them.
        var report = CalibrationReport.Create(results, dataset, prices);
        if (report.IsFailure)
        {
            throw new InvalidOperationException("The results do not fit the dataset: " + string.Join("; ", report.Error.Select(problem => problem.ToString())));
        }

        File.WriteAllText("report.json", report.Value.ToJson());
        File.WriteAllText("report.md", report.Value.ToMarkdown());
        return report.Value;
    }
}
#endregion
