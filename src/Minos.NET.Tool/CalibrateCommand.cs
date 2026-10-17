using System.CommandLine;
using System.Globalization;
using Minos.Evaluation;
using Minos.SystemOne;

namespace Minos.Tool;

/// <summary>The <c>minos calibrate</c> command: runs a dataset against a provider and writes the results and the report.</summary>
internal static class CalibrateCommand
{
    public static Command Create(TextWriter stdout, TextWriter stderr, Func<HttpMessageHandler> network)
    {
        var dataset = CommandSupport.DatasetOption();
        var config = CommandSupport.ConfigOption();
        var provider = CommandSupport.ProviderOption();
        var model = CommandSupport.ModelOption();
        var parallel = new Option<int>("--parallel") { Description = "How many items run at once.", HelpName = "n", DefaultValueFactory = _ => 1 };
        parallel.Validators.Add(result =>
        {
            // Text that is not a number is the parser's own conversion error; only a number below 1 is ours.
            if (result.Tokens.Count == 1
                && int.TryParse(result.Tokens[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                && count < 1)
            {
                result.AddError("--parallel must be 1 or more.");
            }
        });
        var prices = CommandSupport.PricesOption();
        var output = CommandSupport.OutOption("results.jsonl, report.json and report.md", "section being run");

        var command = new Command("calibrate", "Runs a labelled dataset against a provider, then writes the results and a calibration report.")
        {
            dataset, config, provider, model, parallel, prices, output,
        };
        command.SetAction((parse, ct) => RunAsync(
            new Request(
                parse.GetRequiredValue(dataset),
                parse.GetRequiredValue(config),
                parse.GetValue(provider),
                parse.GetValue(model),
                parse.GetValue(parallel),
                parse.GetValue(prices),
                parse.GetValue(output)),
            stdout,
            stderr,
            network,
            ct));
        return command;
    }

    private sealed record Request(string Dataset, string Config, string? Provider, string? Model, int Parallel, string? Prices, string? Out);

    private static Task<int> RunAsync(Request request, TextWriter stdout, TextWriter stderr, Func<HttpMessageHandler> network, CancellationToken ct)
        => CommandSupport.GuardAsync(stderr, "The run failed", () => ExecuteAsync(request, stdout, stderr, network, ct), ct);

    private static async Task<int> ExecuteAsync(Request request, TextWriter stdout, TextWriter stderr, Func<HttpMessageHandler> network, CancellationToken ct)
    {
        var loaded = CommandSupport.ReadInput(() => DecisionDataset.Load(request.Dataset));
        if (!loaded.IsSuccess)
        {
            await CommandSupport.PrintProblemsAsync(stderr, loaded.Error).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        var resolved = ToolConfiguration.Resolve(request.Config, request.Provider, request.Model);
        if (!resolved.IsSuccess)
        {
            await stderr.WriteLineAsync(resolved.Error).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        PriceTable? priceTable = null;
        if (request.Prices is not null)
        {
            var priced = PricesFile.Load(request.Prices);
            if (!priced.IsSuccess)
            {
                await stderr.WriteLineAsync(priced.Error).ConfigureAwait(false);
                return ExitCodes.Usage;
            }

            priceTable = priced.Value;
        }

        var (name, options) = resolved.Value;
        using var http = new HttpClient(network());
        SystemOneDecisionClient client;
        try
        {
            SystemOneDecisionClient.ConfigureHttpClient(http, options);
            client = new SystemOneDecisionClient(http, options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await stderr.WriteLineAsync("The options of provider '" + name + "' are invalid: " + CommandSupport.OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        using (client)
        {
            var outDir = request.Out ?? CommandSupport.NextDefaultOut("minos-out", name, DateTime.UtcNow);
            var runOptions = new DatasetRunOptions
            {
                MaxParallelism = request.Parallel,
                DatasetName = Path.GetFileNameWithoutExtension(request.Dataset) is { Length: > 0 } stem ? stem : "dataset",
            };

            EvaluationResults results;
            DatasetRunException? stopped = null;
            try
            {
                results = await DatasetRunner.RunAsync(client, loaded.Value, runOptions, ct).ConfigureAwait(false);
            }
            catch (DatasetRunException ex) when (!ct.IsCancellationRequested)
            {
                stopped = ex;
                results = ex.PartialResults;
            }

            Directory.CreateDirectory(outDir);
            var resultsFile = Path.Combine(outDir, stopped is null ? "results.jsonl" : "results.partial.jsonl");
            results.Save(resultsFile);

            // A previous run in this directory may have left the other kind of results file; it would contradict this run.
            File.Delete(Path.Combine(outDir, stopped is null ? "results.partial.jsonl" : "results.jsonl"));
            await stdout.WriteLineAsync(resultsFile).ConfigureAwait(false);

            var report = CalibrationReport.Create(results, loaded.Value, priceTable);
            if (!report.IsSuccess)
            {
                await CommandSupport.PrintProblemsAsync(stderr, report.Error).ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            var json = Path.Combine(outDir, "report.json");
            var markdown = Path.Combine(outDir, "report.md");
            await File.WriteAllTextAsync(json, report.Value.ToJson(), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(markdown, report.Value.ToMarkdown(), ct).ConfigureAwait(false);
            await stdout.WriteLineAsync(json).ConfigureAwait(false);
            await stdout.WriteLineAsync(markdown).ConfigureAwait(false);

            if (stopped is not null)
            {
                await stderr.WriteLineAsync(CommandSupport.OneLine(stopped.Message)).ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            return ExitCodes.Success;
        }
    }
}
