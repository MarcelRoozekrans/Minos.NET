using System.Globalization;
using Minos.Evaluation;
using Minos.SystemOne;

namespace Minos.Samples.ShadowReplay;

/// <summary>The command line of the sample: reads the options, builds the two clients, runs the replay and maps the outcome to an exit code.</summary>
internal static class ShadowReplayApp
{
    internal const int Cancelled = 130;

    private static readonly string[] Required = ["--dataset", "--config", "--primary", "--candidate", "--records"];

    /// <summary>Runs the sample.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="stdout">Where the progress and the summary go.</param>
    /// <param name="stderr">Where the problems go.</param>
    /// <param name="cancellationToken">Stops the replay; the records written so far are kept.</param>
    /// <returns>0 when the primary decided an item, 1 when it decided none, 2 for a problem with the command line, the dataset, the configuration or the records file, 130 when stopped.</returns>
    internal static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        var options = ParseOptions(args, stderr);
        if (options is null)
        {
            stderr.WriteLine("Usage: --dataset <jsonl> --config <minos.json> --primary <section> --candidate <section> --records <file>");
            return 2;
        }

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

        if (dataset.Count > ShadowReplayLoop.DailyCap)
        {
            stderr.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"The dataset has {dataset.Count} items, more than the daily cap of {ShadowReplayLoop.DailyCap} shadowed decisions. Items past the cap would not be shadowed."));
            return 2;
        }

        SystemOneDecisionClientOptions primaryOptions;
        SystemOneDecisionClientOptions candidateOptions;
        try
        {
            primaryOptions = ReplayConfiguration.Resolve(options["--config"], options["--primary"]);
            candidateOptions = ReplayConfiguration.Resolve(options["--config"], options["--candidate"]);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("The configuration cannot be used: " + ex.Message);
            return 2;
        }

        // The records file starts fresh: the sink appends, so an earlier run's lines would mix into this one.
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

        using var primaryHttp = new HttpClient();
        using var candidateHttp = new HttpClient();
        SystemOneDecisionClient.ConfigureHttpClient(primaryHttp, primaryOptions);
        SystemOneDecisionClient.ConfigureHttpClient(candidateHttp, candidateOptions);
        using var candidate = new SystemOneDecisionClient(candidateHttp, candidateOptions);

        // The replay disposes the primary too; disposing it twice is harmless, and this covers a failure before it takes over.
        using var primary = new SystemOneDecisionClient(primaryHttp, primaryOptions);
        try
        {
            var summary = await ShadowReplayLoop.RunAsync(primary, candidate, dataset, records, stdout, cancellationToken).ConfigureAwait(false);
            stdout.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"items={summary.Items} decided={summary.Decided} primary_failed={summary.PrimaryFailed} shadowed={summary.Shadowed} waits_run_out={summary.WaitsRunOut}"));
            return summary.Decided > 0 ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("Stopped; the records written so far are kept in " + records + ".");
            return Cancelled;
        }
    }

    // All five options are required; null when one is missing, repeated, unknown or has no value.
    private static Dictionary<string, string>? ParseOptions(string[] args, TextWriter stderr)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!Required.Contains(args[i], StringComparer.Ordinal) || i + 1 >= args.Length || !values.TryAdd(args[i], args[i + 1]))
            {
                stderr.WriteLine("Cannot use the argument '" + args[i] + "'.");
                return null;
            }
        }

        var missing = Required.Where(name => !values.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
        {
            stderr.WriteLine("Missing: " + string.Join(", ", missing) + ".");
            return null;
        }

        return values;
    }
}
