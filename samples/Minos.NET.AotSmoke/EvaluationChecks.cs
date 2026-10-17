using System.Text.Json;
using Minos.Evaluation;
using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// The dataset runner, the results file, the price table and the calibration report under Native AOT: a run over a fake
/// client, a save and a load, hand-built results, price lookups, and the report's summaries, JSON and Markdown.
/// </summary>
internal static class EvaluationChecks
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static string Line(string id, bool expected)
        => "{\"id\":\"" + id + "\",\"state\":\"s " + id + "\",\"questions\":{\"q\":{\"type\":\"noul\",\"instructions\":\"Is it urgent?\"}},\"expected\":{\"q\":" + (expected ? "true" : "false") + "}}";

    private static DecisionDataset Dataset()
    {
        var parsed = DecisionDataset.Parse(string.Join('\n', new[] { Line("a", true), Line("b", true), Line("c", false), Line("d", true) }));
        return parsed.IsSuccess ? parsed.Value : throw new InvalidOperationException("The smoke dataset did not parse.");
    }

    private static Task<EvaluationResults> RunAsync(DecisionDataset dataset, DatasetRunOptions? options = null)
        => DatasetRunner.RunAsync(new AnsweringClient(), dataset, options);

    private static PriceTable Prices()
        => new([new ModelPrice { Model = "jev-1.14", InputPerMillion = 0.042, OutputPerMillion = 0, Date = new DateOnly(2026, 10, 17), Source = "OpenRouter listing" }]);

    [Covers("Minos.Evaluation.DatasetRunOptions.DatasetRunOptions() -> void")]
    [Covers("static Minos.Evaluation.DatasetRunner.RunAsync(Minos.IDecisionClient! client, Minos.Evaluation.DecisionDataset! dataset, Minos.Evaluation.DatasetRunOptions? options = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Minos.Evaluation.EvaluationResults!>!")]
    public static async Task RunsADatasetOverAFakeClient()
    {
        var dataset = Dataset();
        var progress = new CountingProgress();

        var results = await DatasetRunner.RunAsync(
            new AnsweringClient(),
            dataset,
            new DatasetRunOptions { DatasetName = "smoke", MaxParallelism = 2, Progress = progress, TimeProvider = TimeProvider.System }).ConfigureAwait(false);

        Program.Check(
            results.Items.Count == 4
                && string.Equals(results.Header.DatasetName, "smoke", StringComparison.Ordinal)
                && results.Header.ItemCount == 4
                && results.Header.Parallelism == 2
                && string.Equals(results.Items[0].Id, "a", StringComparison.Ordinal)
                && string.Equals(results.Items[3].Id, "d", StringComparison.Ordinal)
                && results.Items[2].Error is null
                && string.Equals(results.Items[2].Model, "jev-1.14-fast", StringComparison.Ordinal)
                && results.Items[2].InputTokens == 1000
                && results.Items[2].Answers is { } answers
                && answers["q"].Noul
                && answers["q"].Confidence > 0.89
                && progress.Last == 4,
            "a dataset runs over a fake client, one result per item in order, under Native AOT");
    }

    [Covers("Minos.Evaluation.EvaluationResults.Save(string! path) -> void")]
    [Covers("Minos.Evaluation.EvaluationResults.Save(System.IO.Stream! utf8Jsonl) -> void")]
    [Covers("static Minos.Evaluation.EvaluationResults.Load(string! path) -> ZeroAlloc.Results.Result<Minos.Evaluation.EvaluationResults!, Minos.Evaluation.DatasetProblems!>")]
    [Covers("static Minos.Evaluation.EvaluationResults.Parse(System.IO.Stream! utf8Jsonl) -> ZeroAlloc.Results.Result<Minos.Evaluation.EvaluationResults!, Minos.Evaluation.DatasetProblems!>")]
    public static async Task ResultsSaveAndLoad()
    {
        var results = await RunAsync(Dataset()).ConfigureAwait(false);
        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            results.Save(path);
            var loaded = EvaluationResults.Load(path);

            using var stream = new MemoryStream();
            results.Save(stream);
            stream.Position = 0;
            var parsed = EvaluationResults.Parse(stream);

            Program.Check(
                loaded.IsSuccess
                    && loaded.Value.Items.Count == 4
                    && string.Equals(loaded.Value.Header.DatasetName, results.Header.DatasetName, StringComparison.Ordinal)
                    && loaded.Value.Items[1].Answers is { } answers
                    && answers["q"].Noul
                    && parsed.IsSuccess
                    && parsed.Value.Items.Count == 4
                    && string.Equals(parsed.Value.Items[3].Id, "d", StringComparison.Ordinal),
                "a results file saves to a path and a stream and loads back under Native AOT");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Covers("Minos.Evaluation.EvaluationResults.EvaluationResults(Minos.Evaluation.RunHeader! header, System.Collections.Generic.IReadOnlyList<Minos.Evaluation.ItemResult!>! items) -> void")]
    [Covers("Minos.Evaluation.ItemError.ItemError() -> void")]
    [Covers("Minos.Evaluation.ItemResult.ItemResult() -> void")]
    [Covers("Minos.Evaluation.RunHeader.RunHeader() -> void")]
    [Covers("static Minos.Evaluation.AnswerRecord.ForChoice(string! key, double confidence) -> Minos.Evaluation.AnswerRecord!")]
    [Covers("static Minos.Evaluation.AnswerRecord.ForNoul(double probability) -> Minos.Evaluation.AnswerRecord!")]
    [Covers("static Minos.Evaluation.AnswerRecord.ForScore(int level, double confidence) -> Minos.Evaluation.AnswerRecord!")]
    public static void HandBuiltResultsRoundTrip()
    {
        var answers = new Dictionary<string, AnswerRecord>(StringComparer.Ordinal)
        {
            ["q"] = AnswerRecord.ForNoul(0.2),
            ["c"] = AnswerRecord.ForChoice("two", 0.6),
            ["s"] = AnswerRecord.ForScore(2, 0.7),
        };
        var answered = new ItemResult
        {
            Id = "a",
            Line = 1,
            LatencyMs = 120,
            Model = "jev-1.14",
            InputTokens = 10,
            OutputTokens = 2,
            Cost = 0.5,
            ResponseId = "r1",
            Answers = answers,
        };
        var failed = new ItemResult { Id = "b", Line = 2, LatencyMs = 50, Error = new ItemError { Kind = "Server", StatusCode = 500, Message = "failed" } };
        var header = new RunHeader
        {
            DatasetName = "hand",
            ItemCount = 2,
            ProviderName = "fake",
            Endpoint = new Uri("https://example.test/"),
            DefaultModel = "jev-1.14",
            Started = Start,
            Finished = Start.AddMinutes(1),
            Parallelism = 1,
        };
        var results = new EvaluationResults(header, [answered, failed]);

        using var stream = new MemoryStream();
        results.Save(stream);
        stream.Position = 0;
        var parsed = EvaluationResults.Parse(stream);

        Program.Check(
            parsed.IsSuccess
                && parsed.Value.Items.Count == 2
                && string.Equals(parsed.Value.Header.DatasetName, "hand", StringComparison.Ordinal)
                && parsed.Value.Header.Started == Start
                && parsed.Value.Items[0].Cost == 0.5
                && parsed.Value.Items[0].Answers is { } loaded
                && !loaded["q"].Noul
                && loaded["q"].Probability == 0.2
                && loaded["q"].Confidence == 0.8
                && string.Equals(loaded["c"].ChoiceKey, "two", StringComparison.Ordinal)
                && loaded["s"].ScoreLevel == 2
                && parsed.Value.Items[1].Error is { } error
                && string.Equals(error.Kind, "Server", StringComparison.Ordinal)
                && error.StatusCode == 500
                && string.Equals(error.Message, "failed", StringComparison.Ordinal),
            "hand-built results, with answers of each kind and an error, round trip through the results file under Native AOT");
    }

    [Covers("Minos.Evaluation.ModelPrice.ModelPrice() -> void")]
    [Covers("Minos.Evaluation.PriceTable.PriceTable(System.Collections.Generic.IEnumerable<Minos.Evaluation.ModelPrice!>! prices) -> void")]
    [Covers("Minos.Evaluation.PriceTable.TryFind(string! model, out Minos.Evaluation.ModelPrice? price) -> bool")]
    public static void PricesMatchTheModelAndItsVariants()
    {
        var table = Prices();

        Program.Check(
            table.Prices.Count == 1
                && table.TryFind("jev-1.14", out var exact)
                && exact is not null
                && exact.InputPerMillion == 0.042
                && table.TryFind("jev-1.14-fast", out var variant)
                && variant is not null
                && string.Equals(variant.Model, "jev-1.14", StringComparison.Ordinal)
                && !table.TryFind("jev-1.140", out var other)
                && other is null,
            "a price table matches a model and its dash variants, and nothing longer, under Native AOT");
    }

    [Covers("Minos.Evaluation.CalibrationReport.ToJson() -> string!")]
    [Covers("Minos.Evaluation.CalibrationReport.ToMarkdown() -> string!")]
    [Covers("static Minos.Evaluation.CalibrationReport.Create(Minos.Evaluation.EvaluationResults! results, Minos.Evaluation.DecisionDataset! dataset, Minos.Evaluation.PriceTable? prices = null) -> ZeroAlloc.Results.Result<Minos.Evaluation.CalibrationReport!, Minos.Evaluation.DatasetProblems!>")]
    public static async Task ReportsCountAndRender()
    {
        var dataset = Dataset();
        var results = await RunAsync(dataset).ConfigureAwait(false);

        var created = CalibrationReport.Create(results, dataset, Prices());
        var report = created.IsSuccess ? created.Value : null;
        var json = report?.ToJson() ?? string.Empty;
        var markdown = report?.ToMarkdown() ?? string.Empty;
        using var document = JsonDocument.Parse(json.Length == 0 ? "{}" : json);
        var root = document.RootElement;

        Program.Check(
            report is not null
                && report.Noul is { } noul
                && noul.Answered == 4
                && noul.Accuracy == 0.75
                && report.Choice is null
                && report.Overall.Scored == 4
                && report.Errors.Count == 0
                && report.Cost.Priced == 4
                && report.Cost.PerThousandDecisions is { } perThousand
                && Math.Abs(perThousand - 0.042) < 1e-9
                && report.Latency.Requests == 4
                && report.ResponseModels.Count == 1
                && root.TryGetProperty("kind", out var kind)
                && string.Equals(kind.GetString(), "minos-report", StringComparison.Ordinal)
                && markdown.StartsWith("# Calibration report", StringComparison.Ordinal),
            "a report counts a run, and writes JSON of kind minos-report and Markdown, under Native AOT");
    }

    public static async Task ReportSummariesKeepTheirValues()
    {
        var dataset = Dataset();
        var results = await RunAsync(dataset).ConfigureAwait(false);
        var failed = new ItemResult { Id = "d", Line = 4, LatencyMs = 1, Error = new ItemError { Kind = "Server", Message = "failed" } };
        var withError = new EvaluationResults(results.Header, [.. results.Items.Take(3), failed]);

        var created = CalibrationReport.Create(withError, dataset, Prices());
        var report = created.IsSuccess ? created.Value : null;

        Program.Check(
            report is not null
                && report.Noul is { } noul
                && noul.Bins.Count == 3
                && noul.Bins[0].Count == 1
                && Math.Abs(noul.Bins[0].MeanConfidence - 0.9) < 1e-9
                && noul.Sweep.Count == 100
                && noul.Sweep[90].Covered == 3
                && noul.Sweep[90].Threshold == 0.9
                && report.Latency.MedianMs is not null
                && report.Cost.PricesUsed.Count == 1
                && report.Cost.Unknown == 0
                && report.Errors.Count == 1
                && report.Errors.ByKind[0].Value == 1
                && report.ResponseModels[0].Count == 3
                && string.Equals(report.ResponseModels[0].Model, "jev-1.14-fast", StringComparison.Ordinal),
            "a report's bins, sweep, latency, cost, errors and models keep their values under Native AOT");
    }

    [Covers("Minos.Evaluation.DatasetRunException.DatasetRunException(Minos.Evaluation.EvaluationResults! partialResults, System.Exception! innerException) -> void")]
    public static async Task AStoppedRunKeepsItsPaidResults()
    {
        var dataset = Dataset();
        DatasetRunException? stopped = null;
        try
        {
            await DatasetRunner.RunAsync(new FaultingClient(), dataset).ConfigureAwait(false);
        }
        catch (DatasetRunException ex)
        {
            stopped = ex;
        }

        var partial = stopped?.PartialResults;
        var rewrapped = partial is null ? null : new DatasetRunException(partial, new InvalidOperationException("again"));
        CalibrationReport? report = null;
        if (partial is not null && CalibrationReport.Create(partial, dataset) is { IsSuccess: true } created)
        {
            report = created.Value;
        }

        Program.Check(
            stopped is { InnerException: InvalidOperationException }
                && partial is { Header: { Partial: true, ItemCount: 2 } }
                && partial.Items.Count == 2
                && ReferenceEquals(rewrapped?.PartialResults, partial)
                && string.Equals(partial.Items[1].Id, "b", StringComparison.Ordinal)
                && report is { Partial: true, DatasetItemCount: 4 }
                && report.ToMarkdown().Contains("Partial run: 2 of 4 items", StringComparison.Ordinal),
            "a stopped run throws its paid results, and they make a partial report, under Native AOT");
    }

    private sealed class CountingProgress : IProgress<int>
    {
        public int Last { get; private set; }

        public void Report(int value) => Last = Math.Max(Last, value);
    }

    // Answers the first two items, then throws.
    private sealed class FaultingClient : IDecisionClient
    {
        private int _calls;

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _calls) > 2
                ? throw new InvalidOperationException("stopped")
                : new(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, [QuestionAnswer.Noul(0.9)])));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class AnsweringClient : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
                request.Definition,
                [QuestionAnswer.Noul(0.9)],
                "jev-1.14-fast",
                new DecisionUsage { InputTokens = 1000, OutputTokens = 0 })));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
