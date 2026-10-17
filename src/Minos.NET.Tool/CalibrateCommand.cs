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
        var dataset = new Option<string>("--dataset") { Description = "The labelled dataset, a JSONL file.", Required = true };
        dataset.Validators.Add(result =>
        {
            var path = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                result.AddError(Directory.Exists(path)
                    ? "The dataset '" + path + "' is a directory, not a file."
                    : "The dataset '" + path + "' does not exist.");
            }
        });
        var config = new Option<string>("--config") { Description = "The config file.", DefaultValueFactory = _ => ToolConfiguration.DefaultFile };
        var provider = new Option<string?>("--provider") { Description = "The provider section; Minos:Active when omitted." };
        var model = new Option<string?>("--model") { Description = "The model, overriding the provider section's." };
        var parallel = new Option<int>("--parallel") { Description = "How many items run at once.", DefaultValueFactory = _ => 1 };
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
        var prices = new Option<string?>("--prices") { Description = "A prices file, for cost when the provider reports none." };
        var output = new Option<string?>("--out") { Description = "The output directory; minos-out/<provider>-<UTC time> when omitted." };

        var command = new Command("calibrate", "Runs a dataset against a provider and writes the results and the report.")
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

    private static async Task<int> RunAsync(Request request, TextWriter stdout, TextWriter stderr, Func<HttpMessageHandler> network, CancellationToken ct)
    {
        try
        {
            return await ExecuteAsync(request, stdout, stderr, network, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is OperationCanceledException or DatasetRunException)
        {
            await stderr.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return ExitCodes.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync("The output cannot be written: " + OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Failure;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync("The run failed: " + OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Failure;
        }
    }

    private static async Task<int> ExecuteAsync(Request request, TextWriter stdout, TextWriter stderr, Func<HttpMessageHandler> network, CancellationToken ct)
    {
        var loaded = DecisionDataset.Load(request.Dataset);
        if (!loaded.IsSuccess)
        {
            await PrintProblemsAsync(stderr, loaded.Error).ConfigureAwait(false);
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
            await stderr.WriteLineAsync("The options of provider '" + name + "' are invalid: " + OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        using (client)
        {
            var outDir = request.Out ?? NextDefaultOut("minos-out", name, DateTime.UtcNow);
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
                await PrintProblemsAsync(stderr, report.Error).ConfigureAwait(false);
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
                await stderr.WriteLineAsync(OneLine(stopped.Message)).ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            return ExitCodes.Success;
        }
    }

    /// <summary>The default output directory, <c>&lt;root&gt;/&lt;provider&gt;-&lt;UTC time&gt;</c>, with -2, -3 and so on when it exists.</summary>
    internal static string NextDefaultOut(string root, string provider, DateTime utcNow)
    {
        var baseName = Path.Combine(root, provider + "-" + utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var candidate = baseName;
        for (var n = 2; Directory.Exists(candidate) || File.Exists(candidate); n++)
        {
            candidate = baseName + "-" + n.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    /// <summary>Makes a message one line, so a failure is one line on stderr.</summary>
    internal static string OneLine(string message)
        => string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static async Task PrintProblemsAsync(TextWriter stderr, DatasetProblems problems)
    {
        foreach (var problem in problems)
        {
            await stderr.WriteLineAsync("line " + problem.Line.ToString(CultureInfo.InvariantCulture) + ": " + problem.Message).ConfigureAwait(false);
        }
    }
}
