using static Minos.Evaluation.Tests.ReportFixture;

namespace Minos.Evaluation.Tests;

public sealed class CalibrationMetricsTests
{
    private const double Tolerance = 1e-9;

    private const string AllKindsQuestions = """
        "questions":{
          "n":{"type":"noul","instructions":"?"},
          "c":{"type":"choice","instructions":"?","criteria":{"one":"1","two":"2","three":"3"}},
          "s":{"type":"score","instructions":"?","criteria":["a","b","c"]}},
          "expected":{"n":true,"c":"two","s":2}}
        """;

    private static string AllKindsLine(string id) => "{\"id\":\"" + id + "\",\"state\":\"x\",\"version\":1," + AllKindsQuestions;

    private static AnswerRecord Choice(string key, double confidence) => AnswerRecord.ForChoice(key, confidence);

    private static AnswerRecord Score(int level, double confidence) => AnswerRecord.ForScore(level, confidence);

    private static void Latency(LatencySummary latency, int requests, double? median, double? p95)
        => Assert.Equal((requests, median, p95), (latency.Requests, latency.MedianMs, latency.P95Ms));

    private static Dictionary<string, AnswerRecord> Q(AnswerRecord answer) => new() { ["q"] = answer };

    private static void Near(double expected, double? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual.Value, expected - Tolerance, expected + Tolerance);
    }

    private static void Bin(ReliabilityBin bin, int count, double meanConfidence, double accuracy)
    {
        Assert.Equal(count, bin.Count);
        Near(meanConfidence, bin.MeanConfidence);
        Near(accuracy, bin.Accuracy);
    }

    private static void Sweep(KindMetrics metrics, int percent, double coverage, double? accuracy, int covered)
    {
        var point = metrics.Sweep[percent];
        Assert.Equal(percent / 100.0, point.Threshold);
        Near(coverage, point.Coverage);
        if (accuracy is { } a)
        {
            Near(a, point.Accuracy);
        }
        else
        {
            Assert.Null(point.Accuracy);
        }

        Assert.Equal(covered, point.Covered);
    }

    private static void AssertSameMetrics(KindMetrics expected, KindMetrics actual)
    {
        Assert.Equal(expected.Scored, actual.Scored);
        Assert.Equal(expected.Answered, actual.Answered);
        Assert.Equal(expected.Accuracy, actual.Accuracy);
        Assert.Equal(expected.BaseRate, actual.BaseRate);
        Assert.Equal(expected.ExpectedCalibrationError, actual.ExpectedCalibrationError);
        Assert.Equal(expected.ExpectedCalibrationErrorEqualWidth, actual.ExpectedCalibrationErrorEqualWidth);
        Assert.Equal(
            expected.Bins.Select(b => (b.Count, b.MeanConfidence, b.Accuracy)),
            actual.Bins.Select(b => (b.Count, b.MeanConfidence, b.Accuracy)));
        Assert.Equal(
            expected.Sweep.Select(p => (p.Threshold, p.Coverage, p.Accuracy, p.Covered)),
            actual.Sweep.Select(p => (p.Threshold, p.Coverage, p.Accuracy, p.Covered)));
    }

    [Fact]
    public void Fixture_MatchesHandComputedValues()
    {
        var report = Report(FixtureResults(), FixtureDataset(), new PriceTable([FixturePrice]));

        var noul = report.Noul!;
        Assert.Equal(14, noul.Scored);
        Assert.Equal(12, noul.Answered);
        Near(9 / 12.0, noul.Accuracy);
        Near(8 / 12.0, noul.BaseRate);
        Near(0.16416666666666666, noul.ExpectedCalibrationError);
        Near(0.18916666666666665, noul.ExpectedCalibrationErrorEqualWidth);

        Assert.Equal(10, noul.Bins.Count);
        Bin(noul.Bins[0], 2, 0.575, 0.5);
        Bin(noul.Bins[1], 2, 0.675, 0.5);
        Bin(noul.Bins[2], 1, 0.75, 1);
        Bin(noul.Bins[3], 1, 0.8, 0);
        Bin(noul.Bins[4], 1, 0.85, 1);
        Bin(noul.Bins[5], 1, 0.9, 1);
        Bin(noul.Bins[6], 1, 0.92, 1);
        Bin(noul.Bins[7], 1, 0.95, 1);
        Bin(noul.Bins[8], 1, 0.97, 1);
        Bin(noul.Bins[9], 1, 0.99, 1);

        Assert.Equal(100, noul.Sweep.Count);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(i / 100.0, noul.Sweep[i].Threshold);
        }

        Sweep(noul, 0, 12 / 14.0, 9 / 12.0, 12);
        Sweep(noul, 50, 12 / 14.0, 9 / 12.0, 12);
        Sweep(noul, 80, 7 / 14.0, 6 / 7.0, 7);
        Sweep(noul, 95, 3 / 14.0, 1.0, 3);
        Sweep(noul, 99, 1 / 14.0, 1.0, 1);

        AssertSameMetrics(noul, report.Overall);
        Assert.Null(report.Choice);
        Assert.Null(report.Score);
        Assert.Equal(0, report.UnscoredQuestions);

        Latency(report.Latency, 12, 600, 1200);

        Assert.Equal(4, report.Cost.Reported);
        Assert.Equal(4, report.Cost.Priced);
        Assert.Equal(4, report.Cost.Unknown);
        Assert.Equal(0, report.Cost.Cached);
        Near(0.521, report.Cost.PerThousandDecisions);
        Assert.Equal(new[] { FixturePrice }, report.Cost.PricesUsed);

        Assert.Equal(2, report.Errors.Count);
        Assert.Equal(new[] { new KeyValuePair<string, int>("Server", 2) }, report.Errors.ByKind);

        Assert.Equal([(null, 8), ("jev-1.14-20261014", 4)], report.ResponseModels.Select(m => (m.Model, m.Count)));
        Assert.Equal("fixture", report.Header.DatasetName);
    }

    [Fact]
    public void FewerThanTenAnswers_OneBinEach()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true), NoulLine(Id(3), false));
        var results = Results(
            Answered(1, Q(Noul(true, 0.9))),
            Answered(2, Q(Noul(false, 0.6))),
            Answered(3, Q(Noul(false, 0.7))));

        var noul = Report(results, dataset).Noul!;

        Assert.Equal(3, noul.Bins.Count);
        Bin(noul.Bins[0], 1, 0.6, 0);
        Bin(noul.Bins[1], 1, 0.7, 1);
        Bin(noul.Bins[2], 1, 0.9, 1);
        Near((0.6 + 0.3 + 0.1) / 3, noul.ExpectedCalibrationError);
    }

    [Fact]
    public void AllOneClass_BaseRateIsOne()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true));
        var results = Results(Answered(1, Q(Noul(true, 0.9))), Answered(2, Q(Noul(false, 0.8))));

        var noul = Report(results, dataset).Noul!;

        Assert.Equal(1.0, noul.BaseRate);
        Assert.Equal(0.5, noul.Accuracy);
    }

    [Fact]
    public void AllConfidenceOne_EceIsOneMinusAccuracy()
    {
        var dataset = Dataset([.. Enumerable.Range(1, 4).Select(i => NoulLine(Id(i), true))]);
        var results = Results(
            Answered(1, Q(Noul(true, 1))),
            Answered(2, Q(Noul(true, 1))),
            Answered(3, Q(Noul(true, 1))),
            Answered(4, Q(Noul(false, 1))));

        var noul = Report(results, dataset).Noul!;

        Near(0.75, noul.Accuracy);
        Near(0.25, noul.ExpectedCalibrationError);
        Near(0.25, noul.ExpectedCalibrationErrorEqualWidth);
        Sweep(noul, 99, 1, 0.75, 4);
    }

    [Fact]
    public void EmptyDataset_HasNoMetrics()
    {
        var report = Report(Results(), Dataset());

        Assert.Null(report.Noul);
        Assert.Null(report.Choice);
        Assert.Null(report.Score);
        var overall = report.Overall;
        Assert.Equal(0, overall.Scored);
        Assert.Equal(0, overall.Answered);
        Assert.Null(overall.Accuracy);
        Assert.Null(overall.BaseRate);
        Assert.Null(overall.ExpectedCalibrationError);
        Assert.Null(overall.ExpectedCalibrationErrorEqualWidth);
        Assert.Empty(overall.Bins);
        Assert.Equal(100, overall.Sweep.Count);
        Assert.All(overall.Sweep, p => Assert.Equal((0.0, (double?)null, 0), (p.Coverage, p.Accuracy, p.Covered)));
        Latency(report.Latency, 0, null, null);
        Assert.Equal(((double?)null, 0, 0, 0, 0), (report.Cost.PerThousandDecisions, report.Cost.Reported, report.Cost.Priced, report.Cost.Unknown, report.Cost.Cached));
        Assert.Empty(report.Cost.PricesUsed);
        Assert.Equal(0, report.Errors.Count);
        Assert.Empty(report.Errors.ByKind);
        Assert.Empty(report.ResponseModels);
    }

    [Fact]
    public void AllFailed_HasNoMetrics_AndCountsTheErrors()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true), NoulLine(Id(3), false));
        var results = Results(Failed(1, "Timeout"), Failed(2, "Server"), Failed(3, "Server"));

        var report = Report(results, dataset);

        var noul = report.Noul!;
        Assert.Equal(3, noul.Scored);
        Assert.Equal(0, noul.Answered);
        Assert.Null(noul.Accuracy);
        Assert.Null(noul.BaseRate);
        Assert.Null(noul.ExpectedCalibrationError);
        Assert.Empty(noul.Bins);
        Assert.All(noul.Sweep, p => Assert.Equal((0.0, (double?)null, 0), (p.Coverage, p.Accuracy, p.Covered)));
        Latency(report.Latency, 0, null, null);
        Assert.Null(report.Cost.PerThousandDecisions);
        Assert.Equal(3, report.Errors.Count);
        Assert.Equal(new[] { new KeyValuePair<string, int>("Server", 2), new KeyValuePair<string, int>("Timeout", 1) }, report.Errors.ByKind);
        Assert.Empty(report.ResponseModels);
    }

    [Fact]
    public void UnscoredQuestionCounted_NotScored()
    {
        const string twoQuestions = """
            {"id":"i1","state":"s","version":1,"questions":{
              "q":{"type":"noul","instructions":"?"},
              "extra":{"type":"noul","instructions":"?"}},
              "expected":{"q":true}}
            """;
        var dataset = Dataset(twoQuestions, NoulLine(Id(2), null));
        var results = Results(
            Answered(1, new Dictionary<string, AnswerRecord> { ["q"] = Noul(true, 0.9), ["extra"] = Noul(false, 0.7) }),
            Answered(2, Q(Noul(true, 0.6))));

        var report = Report(results, dataset);

        Assert.Equal(2, report.UnscoredQuestions);
        Assert.Equal(1, report.Noul!.Scored);
        Assert.Equal(1, report.Noul.Answered);
        Assert.Equal(1.0, report.Noul.Accuracy);
        Assert.Equal(1, report.Overall.Scored);
    }

    [Fact]
    public void ScoredQuestionWithoutAnAnswer_IsNotAnswered()
    {
        const string twoQuestions = """
            {"id":"i1","state":"s","version":1,"questions":{
              "q":{"type":"noul","instructions":"?"},
              "r":{"type":"noul","instructions":"?"}},
              "expected":{"q":true,"r":true}}
            """;
        var dataset = Dataset(twoQuestions);
        var results = Results(Answered(1, Q(Noul(true, 0.9))));

        var noul = Report(results, dataset).Noul!;

        Assert.Equal(2, noul.Scored);
        Assert.Equal(1, noul.Answered);
        Sweep(noul, 0, 0.5, 1.0, 1);
    }

    [Fact]
    public void CachedItem_ExcludedFromLatencyAndCost()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true));
        var results = Results(
            Answered(1, Q(Noul(true, 0.9)), latencyMs: 5, cached: true, cost: 0.5),
            Answered(2, Q(Noul(true, 0.8)), latencyMs: 300, cost: 0.001));

        var report = Report(results, dataset);

        Latency(report.Latency, 1, 300, 300);
        Assert.Equal(1, report.Cost.Cached);
        Assert.Equal(1, report.Cost.Reported);
        Assert.Equal(0, report.Cost.Unknown);
        Near(1.0, report.Cost.PerThousandDecisions);
        Assert.Equal(2, report.Noul!.Answered);
    }

    [Fact]
    public void Latency_IsNearestRank()
    {
        var dataset = Dataset([.. Enumerable.Range(1, 20).Select(i => NoulLine(Id(i), true))]);
        var results = Results([.. Enumerable.Range(1, 20).Select(i => Answered(i, Q(Noul(true, 0.9)), latencyMs: 21 - i))]);

        Latency(Report(results, dataset).Latency, 20, 10, 19);
    }

    [Fact]
    public void ReportedZeroCost_CountsAsReported()
    {
        var dataset = Dataset(NoulLine(Id(1), true));
        var results = Results(Answered(1, Q(Noul(true, 0.9)), cost: 0));

        var cost = Report(results, dataset).Cost;

        Assert.Equal(1, cost.Reported);
        Assert.Equal(0.0, cost.PerThousandDecisions);
    }

    [Fact]
    public void MissingOneTokenCount_IsUnknown()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true));
        var results = Results(
            Answered(1, Q(Noul(true, 0.9)), model: "jev-1.14", inputTokens: 1000),
            Answered(2, Q(Noul(true, 0.9)), model: "jev-1.14", outputTokens: 1000));

        var cost = Report(results, dataset, new PriceTable([FixturePrice])).Cost;

        Assert.Equal(0, cost.Priced);
        Assert.Equal(2, cost.Unknown);
        Assert.Empty(cost.PricesUsed);
    }

    [Fact]
    public void NullModel_IsUnknown()
    {
        var dataset = Dataset(NoulLine(Id(1), true));
        var results = Results(Answered(1, Q(Noul(true, 0.9)), inputTokens: 1000, outputTokens: 10));

        var cost = Report(results, dataset, new PriceTable([FixturePrice])).Cost;

        Assert.Equal(0, cost.Priced);
        Assert.Equal(1, cost.Unknown);
    }

    [Fact]
    public void NoPriceTable_LeavesTokenCostsUnknown()
    {
        var dataset = Dataset(NoulLine(Id(1), true));
        var results = Results(Answered(1, Q(Noul(true, 0.9)), model: "jev-1.14", inputTokens: 1000, outputTokens: 10));

        var cost = Report(results, dataset).Cost;

        Assert.Equal(1, cost.Unknown);
        Assert.Null(cost.PerThousandDecisions);
    }

    [Fact]
    public void PricedCost_UsesBothTokenCounts()
    {
        var dataset = Dataset(NoulLine(Id(1), true));
        var price = Price("m", 2, 10, new DateOnly(2026, 10, 1), "s");
        var results = Results(Answered(1, Q(Noul(true, 0.9)), model: "m-2026", inputTokens: 1000, outputTokens: 500));

        var cost = Report(results, dataset, new PriceTable([price])).Cost;

        Assert.Equal(1, cost.Priced);
        Near((1000 * 2 + 500 * 10) / 1e6 * 1000, cost.PerThousandDecisions);
        Assert.Equal(new[] { price }, cost.PricesUsed);
    }

    [Fact]
    public void ChoiceAndScore_UseConfidence_ExactMatch()
    {
        var dataset = Dataset(AllKindsLine("a"), AllKindsLine("b"));
        var results = Results(
            Answered(1, new Dictionary<string, AnswerRecord> { ["n"] = Noul(true, 0.9), ["c"] = Choice("two", 0.4), ["s"] = Score(1, 0.8) }, id: "a"),
            Answered(2, new Dictionary<string, AnswerRecord> { ["n"] = Noul(false, 0.6), ["c"] = Choice("one", 0.7), ["s"] = Score(2, 0.6) }, id: "b"));

        var report = Report(results, dataset);

        var choice = report.Choice!;
        Assert.Equal(2, choice.Scored);
        Near(0.5, choice.Accuracy);
        Near(1.0, choice.BaseRate);
        Assert.Equal(2, choice.Bins.Count);
        Bin(choice.Bins[0], 1, 0.4, 1);
        Bin(choice.Bins[1], 1, 0.7, 0);
        Sweep(choice, 0, 1, 0.5, 2);
        Sweep(choice, 50, 0.5, 0, 1);

        var score = report.Score!;
        Near(0.5, score.Accuracy);
        Bin(score.Bins[0], 1, 0.6, 1);
        Bin(score.Bins[1], 1, 0.8, 0);
        Near((0.4 + 0.8) / 2, score.ExpectedCalibrationError);
    }

    [Fact]
    public void OverallSweep_CombinesKinds()
    {
        var dataset = Dataset(AllKindsLine("a"), AllKindsLine("b"), AllKindsLine("c"));
        var results = Results(
            Answered(1, new Dictionary<string, AnswerRecord> { ["n"] = Noul(true, 0.9), ["c"] = Choice("two", 0.4), ["s"] = Score(1, 0.8) }, id: "a"),
            Answered(2, new Dictionary<string, AnswerRecord> { ["n"] = Noul(false, 0.6), ["c"] = Choice("one", 0.7), ["s"] = Score(2, 0.6) }, id: "b"),
            Failed(3, id: "c"));

        var report = Report(results, dataset);

        Assert.Equal(3, report.Noul!.Scored);
        Assert.Equal(2, report.Noul.Answered);
        var overall = report.Overall;
        Assert.Equal(9, overall.Scored);
        Assert.Equal(6, overall.Answered);
        Near(0.5, overall.Accuracy);
        Near(1.0, overall.BaseRate);
        Sweep(overall, 0, 6 / 9.0, 0.5, 6);
        Sweep(overall, 50, 5 / 9.0, 2 / 5.0, 5);
        Sweep(overall, 80, 2 / 9.0, 0.5, 2);
        Sweep(overall, 95, 0, null, 0);

        // Ties keep item order, then question order: b.n before b.s at 0.6.
        Assert.Equal(6, overall.Bins.Count);
        Bin(overall.Bins[0], 1, 0.4, 1);
        Bin(overall.Bins[1], 1, 0.6, 0);
        Bin(overall.Bins[2], 1, 0.6, 1);
        Bin(overall.Bins[3], 1, 0.7, 0);
        Bin(overall.Bins[4], 1, 0.8, 0);
        Bin(overall.Bins[5], 1, 0.9, 1);
    }

    [Fact]
    public void BaseRate_IsTheMostCommonExpectedAnswer()
    {
        const string choiceLine = """{"id":"ID","state":"x","version":1,"questions":{"c":{"type":"choice","instructions":"?","criteria":{"one":"1","two":"2"}}},"expected":{"c":"KEY"}}""";
        var dataset = Dataset(
            choiceLine.Replace("ID", "a", StringComparison.Ordinal).Replace("KEY", "one", StringComparison.Ordinal),
            choiceLine.Replace("ID", "b", StringComparison.Ordinal).Replace("KEY", "two", StringComparison.Ordinal),
            choiceLine.Replace("ID", "c", StringComparison.Ordinal).Replace("KEY", "two", StringComparison.Ordinal));
        var answers = new Dictionary<string, AnswerRecord> { ["c"] = Choice("two", 0.5) };
        var results = Results(Answered(1, answers, id: "a"), Answered(2, answers, id: "b"), Answered(3, answers, id: "c"));

        var choice = Report(results, dataset).Choice!;

        Near(2 / 3.0, choice.BaseRate);
        Near(2 / 3.0, choice.Accuracy);
    }

    [Fact]
    public void IdMismatch_IsAProblem()
    {
        var results = FixtureResults();
        var items = results.Items.ToArray();
        items[2] = Answered(3, items[2].Answers!, items[2].LatencyMs, id: "other");

        var result = CalibrationReport.Create(new EvaluationResults(results.Header, items), FixtureDataset());

        Assert.False(result.IsSuccess);
        var problem = Assert.Single(result.Error);
        Assert.Equal(3, problem.Line);
        Assert.Contains("other", problem.Message, StringComparison.Ordinal);
        Assert.Contains("i3", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CountMismatch_IsAProblem()
    {
        var results = FixtureResults();
        var shorter = Results([.. results.Items.Take(13)]);

        var result = CalibrationReport.Create(shorter, FixtureDataset());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Error, p => p.Line == 14 && p.Message.Contains("13", StringComparison.Ordinal) && p.Message.Contains("14", StringComparison.Ordinal));
    }

    [Fact]
    public void HeaderCountMismatch_IsAProblem()
    {
        var results = FixtureResults();
        var header = Header(15);

        var result = CalibrationReport.Create(new EvaluationResults(header, results.Items), FixtureDataset());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Error, p => p.Message.Contains("15", StringComparison.Ordinal));
    }

    // The answers of item b, the second of two all-kinds items, so a problem is on line 2.
    private static DatasetProblem AnswerProblem(Dictionary<string, AnswerRecord> answers)
    {
        var dataset = Dataset(AllKindsLine("a"), AllKindsLine("b"));
        var good = new Dictionary<string, AnswerRecord> { ["n"] = Noul(true, 0.9), ["c"] = Choice("two", 0.4), ["s"] = Score(1, 0.8) };
        var results = Results(Answered(1, good, id: "a"), Answered(2, answers, id: "b"));

        var result = CalibrationReport.Create(results, dataset);

        Assert.False(result.IsSuccess);
        var problem = Assert.Single(result.Error);
        Assert.Equal(2, problem.Line);
        Assert.Contains("'b'", problem.Message, StringComparison.Ordinal);
        return problem;
    }

    [Fact]
    public void AnswerKeyNotAQuestion_IsAProblem()
    {
        var problem = AnswerProblem(new() { ["n"] = Noul(true, 0.9), ["other"] = Noul(true, 0.9) });

        Assert.Contains("'other'", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnswerKindNotTheQuestions_IsAProblem()
    {
        var problem = AnswerProblem(new() { ["n"] = Choice("two", 0.9) });

        Assert.Contains("Choice", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Noul", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChoiceKeyNotAnOption_IsAProblem()
    {
        Assert.Contains("'four'", AnswerProblem(new() { ["c"] = Choice("four", 0.9) }).Message, StringComparison.Ordinal);
        Assert.Contains("'Two'", AnswerProblem(new() { ["c"] = Choice("Two", 0.9) }).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoreLevelOutOfRange_IsAProblem()
    {
        Assert.Contains("level 3", AnswerProblem(new() { ["s"] = Score(3, 0.9) }).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunnerNoulAnswers_SaveLoadAndReport()
    {
        double[] probabilities = [0.3, 0.5, 0.7];
        var dataset = Dataset(NoulLine(Id(1), false), NoulLine(Id(2), true), NoulLine(Id(3), true));
        var next = 0;
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(
            ZeroAlloc.Results.Result<DecisionResponse, DecisionError>.Success(
                new DecisionResponse(r.Definition, [QuestionAnswer.Noul(probabilities[next++])], null, null))));

        var run = await DatasetRunner.RunAsync(client, dataset);
        using var stream = new MemoryStream();
        run.Save(stream);
        stream.Position = 0;
        var loaded = EvaluationResults.Parse(stream);
        Assert.True(loaded.IsSuccess);

        var noul = Report(loaded.Value, dataset).Noul!;

        Assert.Equal(3, noul.Answered);
        Near(1.0, noul.Accuracy);
        Bin(noul.Bins[0], 1, 0.5, 1);
        Bin(noul.Bins[1], 1, 0.7, 1);
        Bin(noul.Bins[2], 1, 0.7, 1);
    }

    [Fact]
    public void BoundaryAnswers_AreNotProblems()
    {
        var dataset = Dataset(AllKindsLine("a"));
        var answers = new Dictionary<string, AnswerRecord> { ["n"] = Noul(false, 1), ["c"] = Choice("three", 0), ["s"] = Score(0, 1) };

        Assert.True(CalibrationReport.Create(Results(Answered(1, answers, id: "a")), dataset).IsSuccess);
    }

    [Fact]
    public void BaseRate_IsPerQuestionKey_NotPooled()
    {
        const string line = """{"id":"ID","state":"x","version":1,"questions":{"c1":{"type":"choice","instructions":"?","criteria":{"one":"1","two":"2"}},"c2":{"type":"choice","instructions":"?","criteria":{"one":"1","two":"2"}}},"expected":{"c1":"E1","c2":"E2"}}""";
        string Line(string id, string e1, string e2) => line
            .Replace("ID", id, StringComparison.Ordinal)
            .Replace("E1", e1, StringComparison.Ordinal)
            .Replace("E2", e2, StringComparison.Ordinal);

        // c1's majority is "one", c2's is "two": pooled over the kind, "one" and "two" tie at 4 of 8.
        var dataset = Dataset(Line("a", "one", "two"), Line("b", "one", "two"), Line("c", "one", "two"), Line("d", "two", "one"));
        var answers = new Dictionary<string, AnswerRecord> { ["c1"] = Choice("one", 0.9), ["c2"] = Choice("one", 0.9) };
        var results = Results([.. "abcd".Select(c => c.ToString()).Select((id, i) => Answered(i + 1, answers, id: id))]);

        var report = Report(results, dataset);

        Near(6 / 8.0, report.Choice!.BaseRate);
        Near(6 / 8.0, report.Overall.BaseRate);
    }

    [Fact]
    public void NoulBaseRate_IsPerQuestionKey()
    {
        const string twoNoul = """{"id":"ID","state":"x","version":1,"questions":{"p":{"type":"noul","instructions":"?"},"r":{"type":"noul","instructions":"?"}},"expected":{"p":EP,"r":ER}}""";
        string Line(string id, bool p, bool r) => twoNoul
            .Replace("ID", id, StringComparison.Ordinal)
            .Replace("EP", p ? "true" : "false", StringComparison.Ordinal)
            .Replace("ER", r ? "true" : "false", StringComparison.Ordinal);

        var dataset = Dataset(Line("a", true, false), Line("b", true, false), Line("c", false, true));
        var answers = new Dictionary<string, AnswerRecord> { ["p"] = Noul(true, 0.9), ["r"] = Noul(true, 0.9) };
        var results = Results([.. "abc".Select(c => c.ToString()).Select((id, i) => Answered(i + 1, answers, id: id))]);

        Near(4 / 6.0, Report(results, dataset).Noul!.BaseRate);
    }

    [Fact]
    public void RunnerNullCost_FallsBackToPrice()
    {
        var dataset = Dataset(NoulLine(Id(1), true));
        var results = Results(Answered(1, Q(Noul(true, 0.9)), model: "jev-1.14", inputTokens: 1000, outputTokens: 10));

        var cost = Report(results, dataset, new PriceTable([FixturePrice])).Cost;

        Assert.Equal((0, 1, 0), (cost.Reported, cost.Priced, cost.Unknown));
        Near(1000 * 0.042 / 1e6 * 1000, cost.PerThousandDecisions);
    }

    private static DatasetProblems CreateProblems(EvaluationResults results, DecisionDataset dataset)
    {
        var result = CalibrationReport.Create(results, dataset);
        Assert.False(result.IsSuccess);
        return result.Error;
    }

    // Two Noul items, the second on line 3 of its file after a blank line, so an item problem shows the dataset line.
    private static DecisionDataset GappedDataset() => Dataset(NoulLine(Id(1), true), string.Empty, NoulLine(Id(2), true));

    private static ItemResult Good(int item) => Answered(item, Q(Noul(true, 0.9)));

    [Fact]
    public void Create_RefusesANonFiniteOrNegativeLatency()
    {
        foreach (var latency in new[] { double.NaN, double.PositiveInfinity, -1 })
        {
            var problems = CreateProblems(Results(Good(1), Answered(2, Q(Noul(true, 0.9)), latencyMs: latency)), GappedDataset());

            var problem = Assert.Single(problems);
            Assert.Equal(3, problem.Line);
            Assert.Contains("LatencyMs", problem.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Create_RefusesAnItemWithNeitherAnswersNorAnError()
    {
        var neither = new ItemResult { Id = Id(2), Line = 3, LatencyMs = 1 };

        var problem = Assert.Single(CreateProblems(Results(Good(1), neither), GappedDataset()));

        Assert.Equal(3, problem.Line);
        Assert.Contains("exactly one of Answers and Error", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesAnItemWithBothAnswersAndAnError()
    {
        var both = new ItemResult { Id = Id(2), Line = 3, LatencyMs = 1, Answers = Q(Noul(true, 0.9)), Error = new ItemError { Kind = "Server", Message = "m" } };

        Assert.Contains("exactly one of Answers and Error", Assert.Single(CreateProblems(Results(Good(1), both), GappedDataset())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesANullItemOrAnswer_WithoutThrowing()
    {
        var nullItem = CreateProblems(Results(Good(1), null!), GappedDataset());
        Assert.Contains(nullItem, p => p.Line == 3 && p.Message.Contains("is null", StringComparison.Ordinal));

        var nullAnswer = CreateProblems(Results(Good(1), Answered(2, new Dictionary<string, AnswerRecord> { ["q"] = null! })), GappedDataset());
        Assert.Contains(nullAnswer, p => p.Line == 3 && p.Message.Contains("Answers[q]", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_RefusesANegativeOrNonFiniteCost_AndNegativeTokens()
    {
        Assert.Contains("Cost", Assert.Single(CreateProblems(Results(Good(1), Answered(2, Q(Noul(true, 0.9)), cost: -0.5)), GappedDataset())).Message, StringComparison.Ordinal);
        Assert.Contains("Cost", Assert.Single(CreateProblems(Results(Good(1), Answered(2, Q(Noul(true, 0.9)), cost: double.NaN)), GappedDataset())).Message, StringComparison.Ordinal);
        Assert.Contains("InputTokens", Assert.Single(CreateProblems(Results(Good(1), Answered(2, Q(Noul(true, 0.9)), inputTokens: -1)), GappedDataset())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesNullErrorMembersAndId()
    {
        var nullKind = new ItemResult { Id = Id(2), Line = 3, LatencyMs = 1, Error = new ItemError { Kind = null!, Message = "m" } };
        Assert.Contains("Error.Kind", Assert.Single(CreateProblems(Results(Good(1), nullKind), GappedDataset())).Message, StringComparison.Ordinal);

        var nullMessage = new ItemResult { Id = Id(2), Line = 3, LatencyMs = 1, Error = new ItemError { Kind = "Server", Message = null! } };
        Assert.Contains("Error.Message", Assert.Single(CreateProblems(Results(Good(1), nullMessage), GappedDataset())).Message, StringComparison.Ordinal);

        var nullId = CreateProblems(Results(Good(1), new ItemResult { Id = null!, Line = 3, LatencyMs = 1, Answers = Q(Noul(true, 0.9)) }), GappedDataset());
        Assert.Contains(nullId, p => p.Line == 3 && p.Message.Contains("Id must be set", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_RefusesAHeaderTheFileCannotHold()
    {
        var results = Results(Good(1), Good(2));
        var header = new RunHeader { DatasetName = string.Empty, ItemCount = 2, Started = Start, Finished = Start, Parallelism = 0 };

        var problems = CreateProblems(new EvaluationResults(header, results.Items), GappedDataset());

        Assert.Contains(problems, p => p.Line == 0 && p.Message.Contains("DatasetName", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Line == 0 && p.Message.Contains("Parallelism", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_AnswerProblemIsOnTheDatasetLine()
    {
        var problem = Assert.Single(CreateProblems(Results(Good(1), Answered(2, new Dictionary<string, AnswerRecord> { ["other"] = Noul(true, 0.9) })), GappedDataset()));

        Assert.Equal(3, problem.Line);
        Assert.Contains("'other'", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_IdMismatchIsOnTheDatasetLine()
    {
        var problem = Assert.Single(CreateProblems(Results(Good(1), Answered(2, Q(Noul(true, 0.9)), id: "zz")), GappedDataset()));

        Assert.Equal(3, problem.Line);
    }

    [Fact]
    public void NoulSweep_TreatsPAndOneMinusPAlike()
    {
        // 1 - 0.07 is 0.9299999999999999 in binary, one step below 0.93; the answer must still be covered at 0.93.
        var dataset = Dataset(NoulLine(Id(1), false), NoulLine(Id(2), true));
        var results = Results(Answered(1, Q(AnswerRecord.ForNoul(0.07))), Answered(2, Q(AnswerRecord.ForNoul(0.93))));

        var noul = Report(results, dataset).Noul!;

        Sweep(noul, 93, 1, 1, 2);
        Sweep(noul, 94, 0, null, 0);
    }

    [Fact]
    public void NoulSweep_CoversABinaryProbabilityWhoseConfidenceClearsTheThreshold()
    {
        // 1 - 0.95 is 0.050000000000000044, above 0.05, yet its confidence is exactly 0.95; 1 - 0.7 likewise.
        var dataset = Dataset(NoulLine(Id(1), false), NoulLine(Id(2), false));
        var results = Results(Answered(1, Q(AnswerRecord.ForNoul(1 - 0.95))), Answered(2, Q(AnswerRecord.ForNoul(1 - 0.7))));

        var noul = Report(results, dataset).Noul!;

        Assert.Equal(0.95, AnswerRecord.ForNoul(1 - 0.95).Confidence);
        Sweep(noul, 95, 0.5, 1, 1);
        Sweep(noul, 70, 1, 1, 2);
        Sweep(noul, 71, 0.5, 1, 1);
    }

    [Fact]
    public void NoulSweep_TheFourMirroredDecimalsStayCovered()
    {
        (double P, int Percent)[] cases = [(0.07, 93), (0.32, 68), (0.33, 67), (0.34, 66)];
        foreach (var (p, percent) in cases)
        {
            var noul = Report(Results(Answered(1, Q(AnswerRecord.ForNoul(p)))), Dataset(NoulLine(Id(1), false))).Noul!;

            Assert.True(noul.Sweep[percent].Covered == 1, $"p = {p} is not covered at {percent}.");
            Assert.True(noul.Sweep[percent + 1].Covered == 0, $"p = {p} is covered above {percent}.");
        }
    }

    [Fact]
    public void NoulSweep_EveryTwoDecimalProbabilityIsCoveredUpToItsOwnConfidence()
    {
        // For p of k hundredths the confidence is max(k, 100 - k) hundredths, so it is covered exactly up to that threshold.
        var lines = Enumerable.Range(0, 101).Select(k => NoulLine(Id(k + 1), true)).ToArray();
        var results = Results([.. Enumerable.Range(0, 101).Select(k => Answered(k + 1, Q(AnswerRecord.ForNoul(k / 100.0))))]);

        var noul = Report(results, Dataset(lines)).Noul!;

        for (var i = 0; i < 100; i++)
        {
            // Every k when i is at most 50; otherwise k from 0 to 100 - i and from i to 100.
            var expected = i <= 50 ? 101 : 2 * (101 - i);
            Assert.True(expected == noul.Sweep[i].Covered, $"At threshold {i}: expected {expected} covered, got {noul.Sweep[i].Covered}.");
        }
    }

    [Fact]
    public void InvalidResponse_IsBilledAndServed_ButNotTimedOrAnswered()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true), NoulLine(Id(3), true), NoulLine(Id(4), true));
        ItemResult Invalid(int item, double? cost, int? input, int? output) => new()
        {
            Id = Id(item),
            Line = item,
            LatencyMs = 5000,
            Model = "jev-1.14-20261014",
            InputTokens = input,
            OutputTokens = output,
            Cost = cost,
            Error = new ItemError { Kind = "InvalidResponse", Message = "The response answered none of the item's questions." },
        };
        var results = Results(
            Answered(1, Q(Noul(true, 0.9)), latencyMs: 100, model: "jev-1.14-20261014", cost: 0.001),
            Invalid(2, 0.003, null, null),
            Invalid(3, null, 1000, 0),
            Failed(4));

        var report = Report(results, dataset, new PriceTable([FixturePrice]));

        Assert.Equal((2, 1, 0, 0), (report.Cost.Reported, report.Cost.Priced, report.Cost.Unknown, report.Cost.Cached));
        Near((0.001 + 0.003 + (1000 * 0.042 / 1e6)) / 3 * 1000, report.Cost.PerThousandDecisions);
        Assert.Equal([("jev-1.14-20261014", 3)], report.ResponseModels.Select(m => (m.Model, m.Count)));
        Latency(report.Latency, 1, 100, 100);
        Assert.Equal((4, 1), (report.Noul!.Scored, report.Noul.Answered));
        Assert.Equal(3, report.Errors.Count);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => CalibrationReport.Create(null!, FixtureDataset()));
        Assert.Throws<ArgumentNullException>(() => CalibrationReport.Create(FixtureResults(), null!));
    }
}
