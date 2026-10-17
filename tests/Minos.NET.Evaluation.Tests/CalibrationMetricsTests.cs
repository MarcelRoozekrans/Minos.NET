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

    private static AnswerRecord Choice(string key, double confidence) => new(QuestionKind.Choice, false, null, key, 0, confidence);

    private static AnswerRecord Score(int level, double confidence) => new(QuestionKind.Score, false, null, null, level, confidence);

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
        Assert.Equal(expected.Bins, actual.Bins);
        Assert.Equal(expected.Sweep, actual.Sweep);
    }

    [Fact]
    public void Fixture_MatchesHandComputedValues()
    {
        var report = Report(FixtureResults(), FixtureDataset(), new PriceTable([JevPrice]));

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

        Assert.Equal(new LatencySummary(12, 600, 1200), report.Latency);

        Assert.Equal(4, report.Cost.Reported);
        Assert.Equal(4, report.Cost.Priced);
        Assert.Equal(4, report.Cost.Unknown);
        Assert.Equal(0, report.Cost.Cached);
        Near(0.521, report.Cost.PerThousandDecisions);
        Assert.Equal(new[] { JevPrice }, report.Cost.PricesUsed);

        Assert.Equal(2, report.Errors.Count);
        Assert.Equal(new[] { new KeyValuePair<string, int>("Server", 2) }, report.Errors.ByKind);

        Assert.Equal(new[] { new ModelCount(null, 8), new ModelCount("jev-1.14-20261014", 4) }, report.ResponseModels);
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
        Assert.Equal(new LatencySummary(0, null, null), report.Latency);
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
        Assert.Equal(new LatencySummary(0, null, null), report.Latency);
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
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true));
        var results = Results(Answered(1, Q(Noul(true, 0.9))), Answered(2, new Dictionary<string, AnswerRecord>()));

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

        Assert.Equal(new LatencySummary(1, 300, 300), report.Latency);
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

        Assert.Equal(new LatencySummary(20, 10, 19), Report(results, dataset).Latency);
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
    public void NegativeReportedCost_IsUnknown()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true));
        var results = Results(
            Answered(1, Q(Noul(true, 0.9)), cost: -0.5),
            Answered(2, Q(Noul(true, 0.9)), cost: double.NaN));

        var cost = Report(results, dataset, new PriceTable([JevPrice])).Cost;

        Assert.Equal(0, cost.Reported);
        Assert.Equal(2, cost.Unknown);
        Assert.Null(cost.PerThousandDecisions);
    }

    [Fact]
    public void MissingOneTokenCount_IsUnknown()
    {
        var dataset = Dataset(NoulLine(Id(1), true), NoulLine(Id(2), true));
        var results = Results(
            Answered(1, Q(Noul(true, 0.9)), model: "jev-1.14", inputTokens: 1000),
            Answered(2, Q(Noul(true, 0.9)), model: "jev-1.14", outputTokens: 1000));

        var cost = Report(results, dataset, new PriceTable([JevPrice])).Cost;

        Assert.Equal(0, cost.Priced);
        Assert.Equal(2, cost.Unknown);
        Assert.Empty(cost.PricesUsed);
    }

    [Fact]
    public void NullModel_IsUnknown()
    {
        var dataset = Dataset(NoulLine(Id(1), true));
        var results = Results(Answered(1, Q(Noul(true, 0.9)), inputTokens: 1000, outputTokens: 10));

        var cost = Report(results, dataset, new PriceTable([JevPrice])).Cost;

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
        var price = new ModelPrice("m", 2, 10, new DateOnly(2026, 10, 1), "s");
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
            Answered(1, new Dictionary<string, AnswerRecord> { ["n"] = Noul(true, 0.9), ["c"] = Choice("two", 0.4), ["s"] = Score(1, 0.8) }) with { Id = "a" },
            Answered(2, new Dictionary<string, AnswerRecord> { ["n"] = Noul(false, 0.6), ["c"] = Choice("one", 0.7), ["s"] = Score(2, 0.6) }) with { Id = "b" });

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
            Answered(1, new Dictionary<string, AnswerRecord> { ["n"] = Noul(true, 0.9), ["c"] = Choice("two", 0.4), ["s"] = Score(1, 0.8) }) with { Id = "a" },
            Answered(2, new Dictionary<string, AnswerRecord> { ["n"] = Noul(false, 0.6), ["c"] = Choice("one", 0.7), ["s"] = Score(2, 0.6) }) with { Id = "b" },
            Failed(3) with { Id = "c" });

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
        var results = Results(Answered(1, answers) with { Id = "a" }, Answered(2, answers) with { Id = "b" }, Answered(3, answers) with { Id = "c" });

        var choice = Report(results, dataset).Choice!;

        Near(2 / 3.0, choice.BaseRate);
        Near(2 / 3.0, choice.Accuracy);
    }

    [Fact]
    public void IdMismatch_IsAProblem()
    {
        var results = FixtureResults();
        var items = results.Items.ToArray();
        items[2] = items[2] with { Id = "other" };

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
        var header = results.Header with { ItemCount = 15 };

        var result = CalibrationReport.Create(new EvaluationResults(header, results.Items), FixtureDataset());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Error, p => p.Message.Contains("15", StringComparison.Ordinal));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => CalibrationReport.Create(null!, FixtureDataset()));
        Assert.Throws<ArgumentNullException>(() => CalibrationReport.Create(FixtureResults(), null!));
    }
}
