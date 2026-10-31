using System.CommandLine;
using System.Globalization;
using System.Text;
using Minos.Evaluation;
using Minos.Recording;

namespace Minos.Tool;

/// <summary>The <c>minos shadow-report</c> command: compares the primary and the candidate of shadowed decisions in a record file.</summary>
internal static class ShadowReportCommand
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
        var prices = CommandSupport.PricesOption();
        var output = CommandSupport.OutOption("shadow-report.json and shadow-report.md", "shadow-report");

        var command = new Command("shadow-report", "Compares the primary and the candidate of shadowed decisions: agreement, confusion tables and calibration against confirmed outcomes.")
        {
            records, prices, output,
        };
        command.SetAction((parse, ct) => CommandSupport.GuardAsync(
            stderr,
            "The shadow report failed",
            () => ExecuteAsync(parse.GetRequiredValue(records), parse.GetValue(prices), parse.GetValue(output), stdout, stderr, ct),
            ct));
        return command;
    }

    private static async Task<int> ExecuteAsync(string recordsPath, string? pricesPath, string? outOption, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
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

        // The whole file is read before the output is opened, so a file that cannot be read is an input problem, and its
        // problems print before the summary.
        var problems = new List<DecisionRecordFileProblem>();
        var report = await CommandSupport.ReadInputAsync(
            () => ShadowReport.CreateAsync(DecisionRecordFile.ReadAsync(recordsPath, problems, ct), priceTable, ct)).ConfigureAwait(false);

        foreach (var problem in problems)
        {
            await stderr.WriteLineAsync("line " + problem.Line.ToString(CultureInfo.InvariantCulture) + ": " + problem.Message).ConfigureAwait(false);
        }

        if (report.DecisionsShadowed == 0)
        {
            await stderr.WriteLineAsync("Nothing to report: no decision in the records has a shadow answer.").ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        ct.ThrowIfCancellationRequested();
        var outDir = CommandSupport.ReserveOut(outOption, "shadow-report");
        var jsonPath = Path.Combine(outDir, "shadow-report.json");
        var markdownPath = Path.Combine(outDir, "shadow-report.md");
        try
        {
            await WriteAsync(jsonPath, report.ToJson(), ct).ConfigureAwait(false);
            await WriteAsync(markdownPath, report.ToMarkdown(), ct).ConfigureAwait(false);
        }
        catch
        {
            // A failed or cancelled report leaves no half-written file behind.
            CommandSupport.DeleteBoth(jsonPath, markdownPath);
            throw;
        }

        var compared = report.Questions.Sum(q => q.Compared);
        await stdout.WriteLineAsync(
            "decisions read: " + report.DecisionsRead.ToString(CultureInfo.InvariantCulture)
            + ", decisions shadowed: " + report.DecisionsShadowed.ToString(CultureInfo.InvariantCulture)
            + ", compared keys: " + compared.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await stdout.WriteLineAsync(jsonPath).ConfigureAwait(false);
        await stdout.WriteLineAsync(markdownPath).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    // The files are made private, as export's are: they are built from recorded state, and the served-model names may be sensitive.
    private static async Task WriteAsync(string path, string text, CancellationToken ct)
    {
        var stream = new FileStream(path, CommandSupport.PrivateOutput());
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(new UTF8Encoding(false).GetBytes(text), ct).ConfigureAwait(false);
        }
    }
}
