using System.CommandLine;
using Minos.Evaluation;
using Minos.Recording;

namespace Minos.Tool;

/// <summary>The <c>minos export</c> command: turns recorded decisions with confirmed outcomes into a labelled dataset and a signals file.</summary>
internal static class ExportCommand
{
    public static Command Create(TextWriter stdout, TextWriter stderr)
    {
        var records = new Option<string>("--records") { Description = "The record file a recorder wrote: a JSONL file of decisions, outcomes and erasures.", HelpName = "file", Required = true };
        records.Validators.Add(result =>
        {
            var path = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                result.AddError(Directory.Exists(path)
                    ? "The records '" + path + "' is a directory, not a file."
                    : "The records '" + path + "' does not exist.");
            }
        });
        var output = CommandSupport.OutOption("dataset.jsonl and dataset.signals.jsonl", "export");

        var command = new Command("export", "Turns recorded decisions with confirmed outcomes into a labelled dataset and a signals file.")
        {
            records, output,
        };
        command.SetAction((parse, ct) => CommandSupport.GuardAsync(
            stderr,
            "The export failed",
            () => ExecuteAsync(parse.GetRequiredValue(records), parse.GetValue(output), stdout, stderr, ct),
            ct));
        return command;
    }

    private static async Task<int> ExecuteAsync(string recordsPath, string? outOption, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var problems = new List<DecisionRecordFileProblem>();
        var decisions = new List<RecordedDecision>();
        try
        {
            await foreach (var decision in DecisionRecordFile.ReadAsync(recordsPath, problems, ct).ConfigureAwait(false))
            {
                decisions.Add(decision);
            }
        }
        catch (FileNotFoundException ex)
        {
            await stderr.WriteLineAsync("The records '" + recordsPath + "' cannot be read: " + CommandSupport.OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        foreach (var problem in problems)
        {
            await stderr.WriteLineAsync("line " + problem.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + problem.Message).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        var outDir = CommandSupport.ReserveOut(outOption, "export");
        var datasetPath = Path.Combine(outDir, "dataset.jsonl");
        var signalsPath = Path.Combine(outDir, "dataset.signals.jsonl");
        DatasetExportSummary summary;
        try
        {
            var dataset = new FileStream(datasetPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using (dataset.ConfigureAwait(false))
            {
                var signals = new FileStream(signalsPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await using (signals.ConfigureAwait(false))
                {
                    summary = await DecisionDatasetExport.WriteAsync(ToAsync(decisions), dataset, signals, ct).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // A failed or cancelled export leaves no half-written dataset behind.
            DeleteBoth(datasetPath, signalsPath);
            throw;
        }

        await stderr.WriteLineAsync(summary.ToString()).ConfigureAwait(false);
        if (summary.ItemsWritten == 0)
        {
            DeleteBoth(datasetPath, signalsPath);
            await stderr.WriteLineAsync("Nothing to export: no decision has a confirmed outcome that makes a dataset item.").ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        await stdout.WriteLineAsync(datasetPath).ConfigureAwait(false);
        await stdout.WriteLineAsync(signalsPath).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static void DeleteBoth(string datasetPath, string signalsPath)
    {
        File.Delete(datasetPath);
        File.Delete(signalsPath);
    }

    private static async IAsyncEnumerable<RecordedDecision> ToAsync(List<RecordedDecision> decisions)
    {
        foreach (var decision in decisions)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return decision;
        }
    }
}
