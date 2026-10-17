using System.Text.Json;
using Minos.Evaluation;
using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// The dataset runner, the results file, the price table and the calibration report under Native AOT: a run over a fake
/// client, a save and a load, hand-built records, price lookups, and the report's JSON and Markdown.
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
        => new([new ModelPrice("jev-1.14", 0.042, 0, new DateOnly(2026, 10, 17), "OpenRouter listing")]);

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

    [Covers("Minos.Evaluation.AnswerRecord.AnswerRecord(Minos.QuestionKind Kind, bool Noul, double? Probability, string? ChoiceKey, int ScoreLevel, double Confidence) -> void")]
    [Covers("Minos.Evaluation.EvaluationResults.EvaluationResults(Minos.Evaluation.RunHeader! header, System.Collections.Generic.IReadOnlyList<Minos.Evaluation.ItemResult!>! items) -> void")]
    [Covers("Minos.Evaluation.ItemError.ItemError(string! Kind, int? StatusCode, string! Message) -> void")]
    [Covers("Minos.Evaluation.ItemResult.ItemResult(string! Id, int Line, double LatencyMs, bool Cached, string? Model, int? InputTokens, int? OutputTokens, double? Cost, string? ResponseId, System.Collections.Generic.IReadOnlyDictionary<string!, Minos.Evaluation.AnswerRecord!>? Answers, Minos.Evaluation.ItemError? Error) -> void")]
    [Covers("Minos.Evaluation.RunHeader.RunHeader(string! DatasetName, int ItemCount, string? ProviderName, System.Uri? Endpoint, string? DefaultModel, System.DateTimeOffset Started, System.DateTimeOffset Finished, int Parallelism) -> void")]
    public static void HandBuiltResultsRoundTrip()
    {
        var answer = new AnswerRecord(QuestionKind.Noul, true, 0.8, null, 0, 0.8);
        var answered = new ItemResult("a", 1, 120, false, "jev-1.14", 10, 2, 0.5, "r1", new Dictionary<string, AnswerRecord>(StringComparer.Ordinal) { ["q"] = answer }, null);
        var failed = new ItemResult("b", 2, 50, false, null, null, null, null, null, null, new ItemError("Server", 500, "failed"));
        var header = new RunHeader("hand", 2, "fake", new Uri("https://example.test/"), "jev-1.14", Start, Start.AddMinutes(1), 1);
        var results = new EvaluationResults(header, [answered, failed]);

        using var stream = new MemoryStream();
        results.Save(stream);
        stream.Position = 0;
        var parsed = EvaluationResults.Parse(stream);

        Program.Check(
            parsed.IsSuccess
                && parsed.Value.Items.Count == 2
                && parsed.Value.Header.Equals(header)
                && parsed.Value.Items[0].Cost == 0.5
                && parsed.Value.Items[0].Answers is { } answers
                && answers["q"].Equals(answer)
                && parsed.Value.Items[1].Error is { } error
                && string.Equals(error.Kind, "Server", StringComparison.Ordinal)
                && error.StatusCode == 500
                && string.Equals(error.Message, "failed", StringComparison.Ordinal),
            "hand-built results, with an answer and an error, round trip through the results file under Native AOT");
    }

    [Covers("Minos.Evaluation.ModelPrice.ModelPrice(string! Model, double InputPerMillion, double OutputPerMillion, System.DateOnly Date, string! Source) -> void")]
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

    [Covers("Minos.Evaluation.CostSummary.CostSummary(double? PerThousandDecisions, int Reported, int Priced, int Unknown, int Cached, System.Collections.Generic.IReadOnlyList<Minos.Evaluation.ModelPrice!>! PricesUsed) -> void")]
    [Covers("Minos.Evaluation.ErrorSummary.ErrorSummary(int Count, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<string!, int>>! ByKind) -> void")]
    [Covers("Minos.Evaluation.KindMetrics.KindMetrics(int Scored, int Answered, double? Accuracy, double? BaseRate, double? ExpectedCalibrationError, double? ExpectedCalibrationErrorEqualWidth, System.Collections.Generic.IReadOnlyList<Minos.Evaluation.ReliabilityBin!>! Bins, System.Collections.Generic.IReadOnlyList<Minos.Evaluation.SweepPoint!>! Sweep) -> void")]
    [Covers("Minos.Evaluation.LatencySummary.LatencySummary(int Requests, double? MedianMs, double? P95Ms) -> void")]
    [Covers("Minos.Evaluation.ModelCount.ModelCount(string? Model, int Count) -> void")]
    [Covers("Minos.Evaluation.ReliabilityBin.ReliabilityBin(int Count, double MeanConfidence, double Accuracy) -> void")]
    [Covers("Minos.Evaluation.SweepPoint.SweepPoint(double Threshold, double Coverage, double? Accuracy, int Covered) -> void")]
    public static void SummariesAreHandBuilt()
    {
        var bin = new ReliabilityBin(2, 0.8, 0.5);
        var sweep = new SweepPoint(0.5, 1, 0.5, 2);
        var kind = new KindMetrics(2, 2, 0.5, 0.5, 0.3, 0.3, [bin], [sweep]);
        var latency = new LatencySummary(2, 100, 200);
        var cost = new CostSummary(0.042, 0, 2, 0, 0, Prices().Prices);
        var errors = new ErrorSummary(1, [new KeyValuePair<string, int>("Server", 1)]);
        var models = new ModelCount("jev-1.14", 2);

        Program.Check(
            bin.Count == 2
                && sweep.Covered == 2
                && kind.Bins.Count == 1
                && kind.Sweep[0].Equals(sweep)
                && latency.P95Ms == 200
                && cost.Priced == 2
                && cost.PricesUsed.Count == 1
                && errors.ByKind[0].Value == 1
                && string.Equals(models.Model, "jev-1.14", StringComparison.Ordinal)
                && models.Count == 2,
            "the report's summaries are built by hand and keep their values under Native AOT");
    }

    private sealed class CountingProgress : IProgress<int>
    {
        public int Last { get; private set; }

        public void Report(int value) => Last = Math.Max(Last, value);
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
