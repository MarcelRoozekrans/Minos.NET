using System.Text;
using System.Text.Json;
using Minos.Evaluation;
using Minos.Recording;
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

    [Covers("static Minos.Evaluation.AnswerRecord.ForChoice(string! key, double confidence, Minos.ProbabilitySource probabilitySource) -> Minos.Evaluation.AnswerRecord!")]
    [Covers("static Minos.Evaluation.AnswerRecord.ForNoul(double probability, Minos.ProbabilitySource probabilitySource) -> Minos.Evaluation.AnswerRecord!")]
    [Covers("static Minos.Evaluation.AnswerRecord.ForScore(int level, double confidence, Minos.ProbabilitySource probabilitySource) -> Minos.Evaluation.AnswerRecord!")]
    public static async Task AnswersKeepTheirProbabilitySource()
    {
        var dataset = Dataset();
        var run = await DatasetRunner.RunAsync(new VerbalizingClient(), dataset).ConfigureAwait(false);
        var created = CalibrationReport.Create(run, dataset);
        var answers = new Dictionary<string, AnswerRecord>(StringComparer.Ordinal)
        {
            ["q"] = AnswerRecord.ForNoul(0.2, ProbabilitySource.Verbalized),
            ["c"] = AnswerRecord.ForChoice("two", 0.6, ProbabilitySource.Logprobs),
            ["s"] = AnswerRecord.ForScore(2, 0.7, ProbabilitySource.Model),
        };
        var header = new RunHeader { DatasetName = "hand", ItemCount = 1, Started = Start, Finished = Start, Parallelism = 1 };
        using var stream = new MemoryStream();
        new EvaluationResults(header, [new ItemResult { Id = "a", Line = 1, LatencyMs = 1, Answers = answers }]).Save(stream);
        stream.Position = 0;
        var parsed = EvaluationResults.Parse(stream);

        Program.Check(
            run.Items[0].Answers is { } recorded
                && recorded["q"].ProbabilitySource == ProbabilitySource.Verbalized
                && created.IsSuccess
                && created.Value.VerbalizedAnswers == 4
                && created.Value.ToMarkdown().Contains("Verbalized probabilities: 4 answers", StringComparison.Ordinal)
                && parsed.IsSuccess
                && parsed.Value.Items[0].Answers is { } loaded
                && loaded["q"].ProbabilitySource == ProbabilitySource.Verbalized
                && loaded["c"].ProbabilitySource == ProbabilitySource.Logprobs
                && loaded["s"].ProbabilitySource == ProbabilitySource.Model,
            "each answer keeps its probability source through a run, the results file and the report under Native AOT");
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

    [Covers("Minos.Evaluation.DatasetRunCanceledException.DatasetRunCanceledException() -> void")]
    [Covers("Minos.Evaluation.DatasetRunCanceledException.DatasetRunCanceledException(Minos.Evaluation.EvaluationResults! partialResults, System.Exception? innerException, System.Threading.CancellationToken cancellationToken) -> void")]
    [Covers("Minos.Evaluation.DatasetRunCanceledException.DatasetRunCanceledException(string! message) -> void")]
    [Covers("Minos.Evaluation.DatasetRunCanceledException.DatasetRunCanceledException(string! message, System.Exception? innerException) -> void")]
    [Covers("Minos.Evaluation.DatasetRunCanceledException.DatasetRunCanceledException(System.Threading.CancellationToken token) -> void")]
    [Covers("Minos.Evaluation.DatasetRunCanceledException.DatasetRunCanceledException(string! message, System.Threading.CancellationToken token) -> void")]
    [Covers("Minos.Evaluation.DatasetRunCanceledException.DatasetRunCanceledException(string! message, System.Exception? innerException, System.Threading.CancellationToken token) -> void")]
    public static async Task ACancelledRunKeepsItsPaidResults()
    {
        var dataset = Dataset();
        using var cts = new CancellationTokenSource();
        OperationCanceledException? caught = null;
        try
        {
            await DatasetRunner.RunAsync(new CancellingClient(cts), dataset, cancellationToken: cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            caught = ex;
        }

        var cancelled = caught as DatasetRunCanceledException;
        var partial = cancelled?.PartialResults;
        var rewrapped = partial is null ? null : new DatasetRunCanceledException(partial, null, cts.Token);
        var inner = new InvalidOperationException("inner");
        DatasetRunCanceledException[] standard =
        [
            new(),
            new("cancelled"),
            new("cancelled", inner),
            new(cts.Token),
            new("cancelled", cts.Token),
            new("cancelled", inner, cts.Token),
        ];
        CalibrationReport? report = null;
        if (partial is not null && CalibrationReport.Create(partial, dataset) is { IsSuccess: true } created)
        {
            report = created.Value;
        }

        Program.Check(
            cancelled is { InnerException: null }
                && partial is { Header: { Partial: true, ItemCount: 2 } }
                && partial.Items.Count == 2
                && string.Equals(partial.Items[1].Id, "b", StringComparison.Ordinal)
                && ReferenceEquals(rewrapped?.PartialResults, partial)
                && rewrapped?.CancellationToken == cts.Token
                && Array.TrueForAll(standard, ex => ex.PartialResults is null)
                && standard[0].Message.Length > 0
                && string.Equals(standard[1].Message, "cancelled", StringComparison.Ordinal)
                && ReferenceEquals(standard[2].InnerException, inner)
                && standard[3].CancellationToken == cts.Token
                && standard[4].CancellationToken == cts.Token
                && ReferenceEquals(standard[5].InnerException, inner)
                && report is { Partial: true, DatasetItemCount: 4 }
                && report.ToMarkdown().Contains("Partial run: 2 of 4 items", StringComparison.Ordinal),
            "a cancelled run throws its paid results as an OperationCanceledException for the caller's token, under Native AOT");
    }

    // The runner reports from its parallel workers, so the highest count is kept with a compare-and-swap.
    private sealed class CountingProgress : IProgress<int>
    {
        private int _last;

        public int Last => Volatile.Read(ref _last);

        public void Report(int value)
        {
            var current = Volatile.Read(ref _last);
            while (value > current)
            {
                var seen = Interlocked.CompareExchange(ref _last, value, current);
                if (seen == current)
                {
                    return;
                }

                current = seen;
            }
        }
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

    // Answers the first two items, then cancels the caller's token and throws for it.
    private sealed class CancellingClient(CancellationTokenSource caller) : IDecisionClient
    {
        private int _calls;

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) > 2)
            {
                caller.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, [QuestionAnswer.Noul(0.9)])));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class VerbalizingClient : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
                request.Definition,
                [QuestionAnswer.Noul(0.9, ProbabilitySource.Verbalized)])));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Covers("static Minos.Evaluation.DecisionDatasetExport.WriteAsync(System.Collections.Generic.IAsyncEnumerable<Minos.Recording.RecordedDecision!>! decisions, System.IO.Stream! dataset, System.IO.Stream? signals, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task<Minos.Evaluation.DatasetExportSummary!>!")]
    public static async Task ExportsRecordedDecisions()
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-export-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        try
        {
            var options = new RecordingOptions { Capacity = 16, FlushTimeout = TimeSpan.FromSeconds(10), Redact = static state => RedactionResult.Keep(state) };
            var recorder = new DecisionRecorder(new JsonlDecisionRecordSink(path), options);
            using (var client = new FixedResponseClient(PipelineChecks.TriageResponse()).AsBuilder().UseRecording(recorder).Build())
            {
                foreach (var id in new[] { "export-1", "export-2" })
                {
                    using var scope = DecisionRecording.Begin(decisionId: id);
                    _ = await client.EvaluateAsync(request).ConfigureAwait(false);
                }

                var at = new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);
                await recorder.AddOutcomeAsync(
                    "export-1",
                    DecisionOutcome.Confirmed("reviewer", at, new Dictionary<string, ConfirmedAnswer>(StringComparer.Ordinal) { ["team"] = ConfirmedAnswer.ForChoice("account") })).ConfigureAwait(false);
                await recorder.AddOutcomeAsync("export-1", DecisionOutcome.Signal("mailer", at, "complained", 1)).ConfigureAwait(false);
                await recorder.FlushAsync().ConfigureAwait(false);
            }

            await recorder.DisposeAsync().ConfigureAwait(false);

            using var dataset = new MemoryStream();
            using var signals = new MemoryStream();
            var summary = await DecisionDatasetExport.WriteAsync(
                DecisionRecordFile.ReadAsync(path, CancellationToken.None), dataset, signals, CancellationToken.None).ConfigureAwait(false);

            var text = Encoding.UTF8.GetString(dataset.ToArray());
            var parsed = DecisionDataset.Parse(text);
            Program.Check(
                summary.DecisionsRead == 2
                    && summary.ItemsWritten == 1
                    && summary.SignalsWritten == 1
                    && summary.SignalsSkipped == 0
                    && summary.SkippedWithoutConfirmedOutcome == 1
                    && summary.SkippedWithImages == 0
                    && summary.SkippedWithoutValidKey == 0
                    && summary.SkippedInvalidItem == 0
                    && summary.KeysDropped == 0
                    && summary.KeysOverridden == 0
                    && string.Equals(summary.ToString(), "decisions read: 2\nitems written: 1\nsignals written: 1\nskipped without a confirmed outcome: 1", StringComparison.Ordinal)
                    && parsed.IsSuccess
                    && parsed.Value.Count == 1
                    && string.Equals(parsed.Value[0].Id, "export-1", StringComparison.Ordinal)
                    && parsed.Value[0].Expected.TryGetValue("team", out var team)
                    && team == ExpectedAnswer.ForChoice("account")
                    && !text.Contains("export-2", StringComparison.Ordinal)
                    && string.Equals(
                        Encoding.UTF8.GetString(signals.ToArray()),
                        "{\"id\":\"export-1\",\"source\":\"mailer\",\"at\":\"2026-10-22T09:30:00+00:00\",\"name\":\"complained\",\"value\":1,\"version\":1}\n",
                        StringComparison.Ordinal),
                "recorded decisions export to a dataset and a signals file, under Native AOT");
        }
        finally
        {
            File.Delete(path);
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
