using System.Globalization;
using Minos.Evaluation;
using Minos.SystemOne;

namespace Minos.Samples.TicketTriage;

/// <summary>The command line of the sample: run mode triages a dataset and writes the records, report mode builds the report from them.</summary>
internal static class TriageApp
{
    internal const int Cancelled = 130;

    /// <summary>The most items one run decides, the cap of the hosted target's daily budget.</summary>
    internal const int MaxItems = 400;

    /// <summary>The fewest whole seconds <c>--budget-seconds</c> takes.</summary>
    internal const int MinBudgetSeconds = 1;

    /// <summary>The most whole seconds <c>--budget-seconds</c> takes.</summary>
    internal const int MaxBudgetSeconds = 300;

    private const string ReportFlag = "--report";
    private const string BudgetOption = "--budget-seconds";
    private const string LocalSection = "Local";
    private const string TypeSafeSection = "TypeSafe";

    private static readonly string[] RunOptions = ["--dataset", "--config", "--records", BudgetOption];
    private static readonly string[] OptionalRunOptions = [BudgetOption];
    private static readonly string[] ReportOptions = ["--records", "--dataset", "--prices", "--out"];

    /// <summary>Runs the sample against the configured providers.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="stdout">Where the progress and the summary go.</param>
    /// <param name="stderr">Where the problems go.</param>
    /// <param name="cancellationToken">Stops the run; the records written so far are kept.</param>
    /// <returns>0 when an item was decided or a report written, 1 when no item was decided, 2 for a problem with the command line, the dataset, the configuration, the records or the prices, 130 when stopped.</returns>
    internal static Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
        => RunAsync(args, stdout, stderr, null, TimeProvider.System, cancellationToken);

    /// <summary>As the overload above, with a factory that builds the client of a config section, so a test can inject fakes.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="stdout">Where the progress and the summary go.</param>
    /// <param name="stderr">Where the problems go.</param>
    /// <param name="factory">Builds the client for a section name and its resolved options; null builds the real client.</param>
    /// <param name="cancellationToken">Stops the run.</param>
    /// <returns>The exit code.</returns>
    internal static Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<string, SystemOneDecisionClientOptions, IDecisionClient>? factory,
        CancellationToken cancellationToken)
        => RunAsync(args, stdout, stderr, factory, TimeProvider.System, cancellationToken);

    /// <summary>As the overload above, with the clock as well, so a test can move it by hand.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="stdout">Where the progress and the summary go.</param>
    /// <param name="stderr">Where the problems go.</param>
    /// <param name="factory">Builds the client for a section name and its resolved options; null builds the real client.</param>
    /// <param name="time">The clock the escalation deadline is read from.</param>
    /// <param name="cancellationToken">Stops the run.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<string, SystemOneDecisionClientOptions, IDecisionClient>? factory,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var parsed = ParseOptions(args, stderr, out var report, out var budget);
        if (parsed is null)
        {
            stderr.WriteLine("Usage: --dataset <jsonl> --config <minos.json> --records <file> [--budget-seconds <1-300>]");
            stderr.WriteLine("   or: --report --records <file> --dataset <jsonl> --prices <prices.json> --out <directory>");
            return 2;
        }

        return report
            ? WriteReport(parsed, stdout, stderr)
            : await RunTriageAsync(parsed, budget, stdout, stderr, factory, time, cancellationToken).ConfigureAwait(false);
    }

    private static int WriteReport(Dictionary<string, string> options, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            TriageReportFiles.Write(options["--records"], options["--dataset"], options["--prices"], options["--out"]);
        }
        catch (InvalidDataException ex)
        {
            stderr.WriteLine("The report cannot be built: " + ex.Message.ReplaceLineEndings(" "));
            return 2;
        }

        stdout.WriteLine("Wrote " + TriageReportFiles.JsonName + " and " + TriageReportFiles.MarkdownName + " to " + Path.GetFullPath(options["--out"]) + ".");
        return 0;
    }

    private static async Task<int> RunTriageAsync(
        Dictionary<string, string> options,
        TimeSpan budget,
        TextWriter stdout,
        TextWriter stderr,
        Func<string, SystemOneDecisionClientOptions, IDecisionClient>? factory,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        DecisionDataset dataset;
        try
        {
            var loaded = DecisionDataset.Load(options["--dataset"]);
            if (!loaded.IsSuccess)
            {
                foreach (var problem in loaded.Error)
                {
                    stderr.WriteLine("line " + problem.Line.ToString(CultureInfo.InvariantCulture) + ": " + problem.Message);
                }

                return 2;
            }

            dataset = loaded.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("The dataset cannot be read: " + ex.Message);
            return 2;
        }

        if (dataset.Count > MaxItems)
        {
            stderr.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"The dataset has {dataset.Count} items, more than the {MaxItems} one triage run takes."));
            return 2;
        }

        SystemOneDecisionClientOptions localOptions;
        SystemOneDecisionClientOptions targetOptions;
        try
        {
            localOptions = TriageConfiguration.Resolve(options["--config"], LocalSection);
            targetOptions = TriageConfiguration.Resolve(options["--config"], TypeSafeSection);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("The configuration cannot be used: " + ex.Message);
            return 2;
        }

        // The records file starts fresh: the run appends, so an earlier run's lines would mix into this one.
        var records = Path.GetFullPath(options["--records"]);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(records)!);
            File.Delete(records);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("The records file '" + records + "' cannot be started: " + ex.Message);
            return 2;
        }

        var https = new List<HttpClient>();
        try
        {
            var build = factory ?? ((_, clientOptions) => CreateClient(clientOptions, https));

            // The run disposes the local client, since the pipeline owns it; the target stays with this method.
            using var target = build(TypeSafeSection, targetOptions);
            var local = build(LocalSection, localOptions);
            try
            {
                var summary = await TriageRun.RunAsync(local, target, dataset, records, stdout, time, cancellationToken, budget).ConfigureAwait(false);
                stdout.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"items={summary.Items} decided={summary.Decided} failed={summary.Failed} escalated_questions={summary.EscalatedQuestions} no_time_left={summary.NoTimeLeft} target_failed={summary.TargetFailed}"));
                return summary.Decided > 0 ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                stderr.WriteLine("Stopped; the records written so far are kept in " + records + ".");
                return Cancelled;
            }
        }
        finally
        {
            foreach (var http in https)
            {
                http.Dispose();
            }
        }
    }

    private static SystemOneDecisionClient CreateClient(SystemOneDecisionClientOptions options, List<HttpClient> https)
    {
        var http = new HttpClient();
        https.Add(http);
        SystemOneDecisionClient.ConfigureHttpClient(http, options);
        return new SystemOneDecisionClient(http, options);
    }

    // Run mode needs its three options and takes an optional budget; report mode, chosen by the --report flag, needs its four.
    // Null when one is missing, repeated, unknown or has no value, or the budget is not a whole number of seconds in range.
    private static Dictionary<string, string>? ParseOptions(string[] args, TextWriter stderr, out bool report, out TimeSpan budget)
    {
        budget = TriagePipeline.Budget;
        report = args.Contains(ReportFlag, StringComparer.Ordinal);
        var allowed = report ? ReportOptions : RunOptions;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = 0;
        for (var i = 0; i < args.Length;)
        {
            if (string.Equals(args[i], ReportFlag, StringComparison.Ordinal))
            {
                if (++flags > 1)
                {
                    stderr.WriteLine("Cannot use the argument '" + args[i] + "' twice.");
                    return null;
                }

                i++;
                continue;
            }

            if (!allowed.Contains(args[i], StringComparer.Ordinal) || i + 1 >= args.Length || !values.TryAdd(args[i], args[i + 1]))
            {
                stderr.WriteLine("Cannot use the argument '" + args[i] + "'.");
                return null;
            }

            if (string.Equals(args[i], BudgetOption, StringComparison.Ordinal))
            {
                if (!int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                    || seconds < MinBudgetSeconds || seconds > MaxBudgetSeconds)
                {
                    stderr.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The option '{BudgetOption}' needs a whole number of seconds from {MinBudgetSeconds} to {MaxBudgetSeconds}."));
                    return null;
                }

                budget = TimeSpan.FromSeconds(seconds);
                i += 2;
                continue;
            }

            // A value that looks like an option means the real value is missing; every other value here is a path.
            if (!IsUsablePath(args[i + 1]))
            {
                stderr.WriteLine("The option '" + args[i] + "' needs a path as its value.");
                return null;
            }

            i += 2;
        }

        var missing = allowed.Where(name => !values.ContainsKey(name) && !OptionalRunOptions.Contains(name, StringComparer.Ordinal)).ToArray();
        if (missing.Length > 0)
        {
            stderr.WriteLine("Missing: " + string.Join(", ", missing) + ".");
            return null;
        }

        return values;
    }

    private static bool IsUsablePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = Path.GetFullPath(value);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
