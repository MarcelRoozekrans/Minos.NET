using System.Globalization;
using System.Runtime.InteropServices;
using Minos.Evaluation;
using Minos.Samples.ShadowReplay;
using Minos.SystemOne;

const int Cancelled = 130;
const string Usage = "Usage: --dataset <jsonl> --config <minos.json> --primary <section> --candidate <section> --records <file>";

var options = ParseOptions(args);
if (options is null)
{
    Console.Error.WriteLine(Usage);
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
            Console.Error.WriteLine("line " + problem.Line.ToString(CultureInfo.InvariantCulture) + ": " + problem.Message);
        }

        return 2;
    }

    dataset = loaded.Value;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("The dataset cannot be read: " + ex.Message);
    return 2;
}

SystemOneDecisionClientOptions primaryOptions;
SystemOneDecisionClientOptions candidateOptions;
try
{
    primaryOptions = ReplayConfiguration.Resolve(options["--config"], options["--primary"]);
    candidateOptions = ReplayConfiguration.Resolve(options["--config"], options["--candidate"]);
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("The configuration cannot be used: " + ex.Message);
    return 2;
}

// The first Ctrl+C or SIGTERM cancels the replay, which flushes and keeps the records it has written.
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Cancel(stop);
};
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    Cancel(stop);
});

// The records file starts fresh: the sink appends, so an earlier run's lines would mix into this one.
var records = Path.GetFullPath(options["--records"]);
Directory.CreateDirectory(Path.GetDirectoryName(records)!);
File.Delete(records);

using var primaryHttp = new HttpClient();
using var candidateHttp = new HttpClient();
SystemOneDecisionClient.ConfigureHttpClient(primaryHttp, primaryOptions);
SystemOneDecisionClient.ConfigureHttpClient(candidateHttp, candidateOptions);
using var candidate = new SystemOneDecisionClient(candidateHttp, candidateOptions);
var primary = new SystemOneDecisionClient(primaryHttp, primaryOptions);

try
{
    var summary = await ShadowReplayLoop.RunAsync(primary, candidate, dataset, records, Console.Out, stop.Token).ConfigureAwait(false);
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"items={summary.Items} decided={summary.Decided} primary_failed={summary.PrimaryFailed} shadowed={summary.Shadowed} waits_run_out={summary.WaitsRunOut}"));
    return summary.Decided > 0 ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Stopped; the records written so far are kept in " + records + ".");
    return Cancelled;
}

static void Cancel(CancellationTokenSource source)
{
    try
    {
        source.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // The replay has already finished.
    }
}

// All five options are required; null when one is missing, repeated, unknown or has no value.
static Dictionary<string, string>? ParseOptions(string[] args)
{
    string[] required = ["--dataset", "--config", "--primary", "--candidate", "--records"];
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (!required.Contains(args[i], StringComparer.Ordinal) || i + 1 >= args.Length || !values.TryAdd(args[i], args[i + 1]))
        {
            Console.Error.WriteLine("Cannot use the argument '" + args[i] + "'.");
            return null;
        }
    }

    var missing = required.Where(name => !values.ContainsKey(name)).ToArray();
    if (missing.Length > 0)
    {
        Console.Error.WriteLine("Missing: " + string.Join(", ", missing) + ".");
        return null;
    }

    return values;
}
