using System.CommandLine;
using Minos.Evaluation;

namespace Minos.Tool;

/// <summary>The <c>minos report</c> command: builds the calibration report again from saved results, without calling a provider.</summary>
internal static class ReportCommand
{
    public static Command Create(TextWriter stdout, TextWriter stderr)
    {
        var results = new Option<string>("--results") { Description = "The results file a calibrate run saved, results.jsonl or results.partial.jsonl.", HelpName = "file", Required = true };
        results.Validators.Add(result =>
        {
            var path = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                result.AddError(Directory.Exists(path)
                    ? "The results '" + path + "' is a directory, not a file."
                    : "The results '" + path + "' does not exist.");
            }
        });
        var dataset = CommandSupport.DatasetOption();
        dataset.Description = "The dataset the results were run on: a JSONL file.";
        var prices = CommandSupport.PricesOption();
        var output = CommandSupport.OutOption("report.json and report.md", "named in the results header");

        var command = new Command("report", "Builds the calibration report again from saved results and their dataset, without calling a provider.")
        {
            results, dataset, prices, output,
        };
        command.SetAction((parse, ct) => CommandSupport.GuardAsync(
            stderr,
            "The report failed",
            () => ExecuteAsync(parse.GetRequiredValue(results), parse.GetRequiredValue(dataset), parse.GetValue(prices), parse.GetValue(output), stdout, stderr, ct),
            ct));
        return command;
    }

    private static async Task<int> ExecuteAsync(string resultsPath, string datasetPath, string? pricesPath, string? outOption, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var results = EvaluationResults.Load(resultsPath);
        if (!results.IsSuccess)
        {
            await CommandSupport.PrintProblemsAsync(stderr, results.Error).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        var dataset = DecisionDataset.Load(datasetPath);
        if (!dataset.IsSuccess)
        {
            await CommandSupport.PrintProblemsAsync(stderr, dataset.Error).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        PriceTable? priceTable = null;
        if (pricesPath is not null)
        {
            var priced = PricesFile.Load(pricesPath);
            if (!priced.IsSuccess)
            {
                await stderr.WriteLineAsync(priced.Error).ConfigureAwait(false);
                return ExitCodes.Usage;
            }

            priceTable = priced.Value;
        }

        var report = CalibrationReport.Create(results.Value, dataset.Value, priceTable);
        if (!report.IsSuccess)
        {
            await CommandSupport.PrintProblemsAsync(stderr, report.Error).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        var provider = CommandSupport.SafeFileName(results.Value.Header.ProviderName);
        var outDir = outOption ?? CommandSupport.NextDefaultOut("minos-out", provider, DateTime.UtcNow);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outDir);
        var json = Path.Combine(outDir, "report.json");
        var markdown = Path.Combine(outDir, "report.md");
        await File.WriteAllTextAsync(json, report.Value.ToJson(), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdown, report.Value.ToMarkdown(), ct).ConfigureAwait(false);
        await stdout.WriteLineAsync(json).ConfigureAwait(false);
        await stdout.WriteLineAsync(markdown).ConfigureAwait(false);
        return ExitCodes.Success;
    }
}
