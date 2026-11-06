using Minos.Evaluation;
using Minos.Samples.TicketTriage;

namespace Minos.Samples.Tests;

public sealed class TriageReportTests
{
    private static readonly PriceTable NoPrices = new([]);

    // Every dataset item asks one Choice question "intent" (right answer a) and one Noul question "urgent" (right answer yes).
    private static DecisionDataset Dataset(params string[] ids)
    {
        var lines = ids.Select(id =>
            """{"id":"ITEM","state":"s","questions":{"intent":{"type":"choice","instructions":"i","criteria":{"a":{"description":"A"},"b":{"description":"B"}}},"urgent":{"type":"noul","instructions":"u"}},"expected":{"intent":"a","urgent":true}}""".Replace("ITEM", id, StringComparison.Ordinal));
        var result = DecisionDataset.Parse(string.Join("\n", lines));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static bool IsIntent(string key) => string.Equals(key, "intent", StringComparison.Ordinal);

    private static TriageAnswer Right(string key) => IsIntent(key) ? new("choice", null, "a", 0.9) : new("noul", 0.9, null, null);

    private static TriageAnswer Wrong(string key) => IsIntent(key) ? new("choice", null, "b", 0.9) : new("noul", 0.1, null, null);

    private static TriageQuestion Q(string key, bool localRight, string reason, bool finalRight)
        => new(key, localRight ? Right(key) : Wrong(key), string.Equals(reason, "Escalated", StringComparison.Ordinal) ? "typesafe" : "local", reason, 0.5, null, finalRight ? Right(key) : Wrong(key));

    private static TriageRecord Rec(string id, TriageQuestion[] questions, params TriageCall[] calls)
        => new(id, 10, null, questions, calls);

    private static TriageCall Local() => new("local", null, null, null, null, "Answered", 1);

    private static TriageCall Target(string status = "Answered", string? model = "m-1") => new("typesafe", model, 10, 2, null, status, 5);

    [Fact]
    public void Split_CountsEachReason_PerKindAndOverall()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", true, "Confident", true), Q("urgent", true, "Escalated", true)], Local(), Target()),
            Rec("2", [Q("intent", false, "NoTimeLeft", false), Q("urgent", false, "TargetFailed", false)], Local(), Target("Failed")),
            Rec("3", [Q("intent", true, "Escalated", true), Q("urgent", true, "Confident", true)], Local(), Target()),
        };

        var split = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), NoPrices).Split;

        Assert.Equal(new SplitCounts(1, 1, 1, 0, 1), split.Choice);
        Assert.Equal(new SplitCounts(1, 1, 0, 1, 2), split.Noul);
        Assert.Equal(new SplitCounts(2, 2, 1, 1, 3), split.All);
    }

    [Fact]
    public void Split_CountsTicketsWithATargetCall()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", true, "Escalated", true)], Local(), Target()),
            Rec("2", [Q("intent", true, "NoTimeLeft", true)], Local(), Target("NotCalledNoTimeLeft")),
            Rec("3", [Q("intent", true, "Confident", true)], Local()),
        };

        var split = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), NoPrices).Split;

        Assert.Equal(1, split.All.TicketsWithTargetCall);
    }

    [Fact]
    public void Accuracy_LocalAlone_UsesTheLocalAnswerForEveryQuestion()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", true, "Confident", true), Q("urgent", false, "Escalated", true)], Local(), Target()),
            Rec("2", [Q("intent", false, "Escalated", true), Q("urgent", false, "Confident", false)], Local(), Target()),
        };

        var accuracy = TriageReportBuilder.Build(records, Dataset("1", "2"), NoPrices).Accuracy;

        Assert.Equal(new Hits(1, 2), accuracy.Choice.LocalAlone);
        Assert.Equal(new Hits(0, 2), accuracy.Noul.LocalAlone);
        Assert.Equal(new Hits(1, 4), accuracy.All.LocalAlone);
        Assert.Equal(0.25, accuracy.All.LocalAlone.Rate);
    }

    [Fact]
    public void Accuracy_Final_UsesTheTargetAnswerOnlyWhereItReplaced()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", false, "Escalated", true), Q("urgent", false, "NoTimeLeft", false)], Local(), Target()),
        };

        var accuracy = TriageReportBuilder.Build(records, Dataset("1"), NoPrices).Accuracy;

        Assert.Equal(new Hits(1, 1), accuracy.Choice.Final);
        Assert.Equal(new Hits(0, 1), accuracy.Noul.Final);
        Assert.Equal(new Hits(1, 2), accuracy.All.Final);
        Assert.Equal(new Hits(1, 1), accuracy.All.Target);
    }

    [Fact]
    public void Escalated_TwoByTwo_ClassifiesFixedBrokenBothRightBothWrong()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", false, "Escalated", true), Q("urgent", true, "Escalated", false)], Local(), Target()),
            Rec("2", [Q("intent", true, "Escalated", true), Q("urgent", false, "Escalated", false)], Local(), Target()),
            Rec("3", [Q("intent", false, "Escalated", false), Q("urgent", true, "Confident", true)], Local(), Target()),
        };

        var escalated = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), NoPrices).Escalated;

        Assert.Equal(new EscalatedTable(1, 0, 1, 1), escalated.Choice);
        Assert.Equal(new EscalatedTable(0, 1, 0, 1), escalated.Noul);
        Assert.Equal(new EscalatedTable(1, 1, 1, 2), escalated.All);
    }

    [Fact]
    public void Escalated_TargetFailed_IsNotCreditedToTheTarget()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", true, "TargetFailed", true), Q("urgent", false, "NoTimeLeft", false)], Local(), Target("Failed")),
        };

        var report = TriageReportBuilder.Build(records, Dataset("1"), NoPrices);

        Assert.Equal(new EscalatedTable(0, 0, 0, 0), report.Escalated.All);
        Assert.Equal(new Hits(0, 0), report.Accuracy.All.Target);
        Assert.Null(report.Accuracy.All.Target.Rate);
    }

    [Fact]
    public void SkippedOrFailedTarget_WithADifferingFinal_IsNotCreditedToTheTarget()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", false, "NoTimeLeft", true), Q("urgent", true, "TargetFailed", false)], Local(), Target("Failed")),
        };

        var report = TriageReportBuilder.Build(records, Dataset("1"), NoPrices);

        Assert.Equal(new Hits(0, 1), report.Accuracy.Choice.Final);
        Assert.Equal(new Hits(1, 1), report.Accuracy.Noul.Final);
        Assert.Equal(new Hits(1, 2), report.Accuracy.All.Final);
        Assert.Equal(new Hits(0, 0), report.Accuracy.All.Target);
        Assert.Equal(new EscalatedTable(0, 0, 0, 0), report.Escalated.All);
    }

    [Fact]
    public void Signal_CountsCaughtAndMissedWrongAnswers_ByReason()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", false, "Escalated", true), Q("urgent", false, "Confident", false)], Local(), Target()),
            Rec("2", [Q("intent", false, "NoTimeLeft", false), Q("urgent", true, "Confident", true)], Local()),
            Rec("3", [Q("intent", false, "TargetFailed", false), Q("urgent", false, "Confident", false)], Local(), Target("Failed")),
        };

        var signal = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), NoPrices).Signal;

        Assert.Equal(3, signal.Choice.WrongCaught);
        Assert.Equal(0, signal.Choice.WrongMissed);
        Assert.Equal(0, signal.Noul.WrongCaught);
        Assert.Equal(2, signal.Noul.WrongMissed);
        Assert.Equal(3, signal.All.WrongCaught);
        Assert.Equal(2, signal.All.WrongMissed);
    }

    [Fact]
    public void Signal_BelowThreshold_CountsHowManyTheLocalModelHadRight()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", true, "Escalated", true), Q("urgent", true, "Confident", true)], Local(), Target()),
            Rec("2", [Q("intent", false, "Escalated", true), Q("urgent", true, "NoTimeLeft", true)], Local(), Target()),
            Rec("3", [Q("intent", true, "TargetFailed", true), Q("urgent", false, "Confident", false)], Local(), Target("Failed")),
        };

        var signal = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), NoPrices).Signal;

        Assert.Equal(new SignalSection(1, 0, 3, 2), signal.Choice);
        Assert.Equal(new SignalSection(0, 1, 1, 1), signal.Noul);
        Assert.Equal(new SignalSection(1, 1, 4, 3), signal.All);
    }

    [Fact]
    public void Run_CountsFailuresByErrorKind_AndServedModels()
    {
        var records = new[]
        {
            Rec("1", [Q("intent", true, "Escalated", true)], Local(), Target(model: "m-b")),
            Rec("2", [Q("intent", true, "Escalated", true)], Local(), Target(model: "m-a")),
            Rec("3", [Q("intent", true, "Escalated", true)], Local(), Target(model: "m-b")),
            new TriageRecord("4", 3, "Timeout", [], []),
            new TriageRecord("5", 3, "Timeout", [], []),
            new TriageRecord("6", 3, "Unauthorized", [], []),
        };

        var run = TriageReportBuilder.Build(records, Dataset("1", "2", "3", "4", "5", "6"), NoPrices).Run;

        Assert.Equal(6, run.ItemsRead);
        Assert.Equal(6, run.DatasetItems);
        Assert.Equal(3, run.Decided);
        Assert.Equal(2, run.FailuresByError["Timeout"]);
        Assert.Equal(1, run.FailuresByError["Unauthorized"]);
        Assert.Equal(2, run.ServedModels["m-b"]);
        Assert.Equal(1, run.ServedModels["m-a"]);
    }

    [Fact]
    public void Build_WithAPartialRun_KeepsTheDatasetSizeApartFromTheItemsRead()
    {
        var records = new[] { new TriageRecord("1", 3, "Timeout", [], []), new TriageRecord("2", 3, "Timeout", [], []) };

        var run = TriageReportBuilder.Build(records, Dataset("1", "2", "3", "4", "5"), NoPrices).Run;

        Assert.Equal(2, run.ItemsRead);
        Assert.Equal(5, run.DatasetItems);
    }

    [Fact]
    public void Build_WithEveryItemFailed_HasZeroDecidedAndNoDivision()
    {
        var records = new[] { new TriageRecord("1", 3, "Timeout", [], []), new TriageRecord("2", 3, "Timeout", [], []) };

        var report = TriageReportBuilder.Build(records, Dataset("1", "2"), NoPrices);

        Assert.Equal(0, report.Run.Decided);
        Assert.Equal(2, report.Run.FailuresByError["Timeout"]);
        Assert.Null(report.Accuracy.All.LocalAlone.Rate);
        Assert.Null(report.Accuracy.All.Final.Rate);
        Assert.Null(report.Accuracy.All.Target.Rate);
        Assert.Null(report.Accuracy.Choice.LocalAlone.Rate);
        Assert.Equal(new SignalSection(0, 0, 0, 0), report.Signal.All);
        Assert.Equal(new SplitCounts(0, 0, 0, 0, 0), report.Split.All);
    }

    [Fact]
    public void Build_WithAnIdNotInTheDataset_NamesTheId()
    {
        var records = new[] { Rec("missing", [Q("intent", true, "Confident", true)], Local()) };

        var ex = Assert.Throws<InvalidOperationException>(() => TriageReportBuilder.Build(records, Dataset("1"), NoPrices));

        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_NoulAtExactlyHalf_IsYes()
    {
        var half = new TriageAnswer("noul", 0.5, null, null);

        Assert.True(Judge.IsRight(half, ExpectedAnswer.ForNoul(true)));
        Assert.False(Judge.IsRight(half, ExpectedAnswer.ForNoul(false)));
    }

    [Fact]
    public void Judge_Choice_ComparesTheKey()
    {
        var answer = new TriageAnswer("choice", null, "a", 0.6);

        Assert.True(Judge.IsRight(answer, ExpectedAnswer.ForChoice("a")));
        Assert.False(Judge.IsRight(answer, ExpectedAnswer.ForChoice("b")));
        Assert.False(Judge.IsRight(answer, ExpectedAnswer.ForNoul(true)));
    }

    private static TriageRecord Timed(string id, double totalMs, params TriageCall[] calls)
        => new(id, totalMs, null, [Q("intent", true, "Confident", true)], calls);

    private static TriageCall Timed(string client, string status, double? ms, string? model = null, int? input = null, int? output = null, double? cost = null)
        => new(client, model, input, output, cost, status, ms);

    [Fact]
    public void Latency_UsesNearestRank_ForMedianAndP95()
    {
        var records = Enumerable.Range(1, 20).Select(i => Timed(i.ToString(System.Globalization.CultureInfo.InvariantCulture), i * 10, Timed("local", "Answered", 1))).ToArray();

        var latency = TriageReportBuilder.Build(records, Dataset(records.Select(r => r.Id).ToArray()), NoPrices).Latency;

        Assert.Equal(20, latency.WithoutTarget.Count);
        Assert.Equal(100, latency.WithoutTarget.Median);
        Assert.Equal(190, latency.WithoutTarget.P95);
    }

    [Fact]
    public void Latency_SplitsTicketsWithAndWithoutATargetCall()
    {
        var records = new[]
        {
            Timed("1", 400, Timed("local", "Answered", 100), Timed("typesafe", "Answered", 300)),
            Timed("2", 450, Timed("local", "Answered", 100), Timed("typesafe", "Failed", 350)),
            Timed("3", 50, Timed("local", "Answered", 50)),
            Timed("4", 60, Timed("local", "Answered", 60), Timed("typesafe", "NotCalledNoTimeLeft", null)),
        };

        var latency = TriageReportBuilder.Build(records, Dataset("1", "2", "3", "4"), NoPrices).Latency;

        Assert.Equal(new TimeSummary(2, 400, 450), latency.WithTarget);
        Assert.Equal(new TimeSummary(2, 50, 60), latency.WithoutTarget);
    }

    [Fact]
    public void Latency_LocalAndTargetCalls_LeaveOutSkippedAndFailedTickets()
    {
        var records = new[]
        {
            Timed("1", 400, Timed("local", "Answered", 100), Timed("typesafe", "Answered", 300)),
            Timed("2", 60, Timed("local", "Answered", 60), Timed("typesafe", "NotCalledNoTimeLeft", null)),
            new TriageRecord("3", 9999, "Timeout", [], []),
        };

        var latency = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), NoPrices).Latency;

        Assert.Equal(new TimeSummary(2, 60, 100), latency.Local);
        Assert.Equal(new TimeSummary(1, 300, 300), latency.Target);
        Assert.Equal(1, latency.WithTarget.Count);
        Assert.Equal(1, latency.WithoutTarget.Count);
    }

    [Fact]
    public void Latency_AnAllFailedRun_HasNoValuesAndStillBuilds()
    {
        var records = new[] { new TriageRecord("1", 5, "Timeout", [], []) };

        var latency = TriageReportBuilder.Build(records, Dataset("1"), NoPrices).Latency;

        Assert.Equal(new TimeSummary(0, null, null), latency.Local);
        Assert.Null(TriageReportBuilder.Build(records, Dataset("1"), NoPrices).Cost.PerThousandTickets);
    }

    private static PriceTable Prices() => new([
        new ModelPrice { Model = "zeta", InputPerMillion = 1000, OutputPerMillion = 2000, Date = new DateOnly(2026, 1, 1), Source = "s" },
        new ModelPrice { Model = "local-m", InputPerMillion = 0, OutputPerMillion = 0, Date = new DateOnly(2026, 1, 1), Source = "s" },
        new ModelPrice { Model = "alpha", InputPerMillion = 1, OutputPerMillion = 1, Date = new DateOnly(2026, 1, 1), Source = "s" },
    ]);

    [Fact]
    public void Cost_SumsATicketsCalls_AndDividesByDecidedTickets()
    {
        var records = new[]
        {
            Timed("1", 1, Timed("local", "Answered", 1, cost: 0.001), Timed("typesafe", "Answered", 1, cost: 0.002)),
            Timed("2", 1, Timed("local", "Answered", 1, cost: 0.003)),
            new TriageRecord("3", 1, "Timeout", [], []),
        };

        var cost = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), NoPrices).Cost;

        Assert.Equal(3.0, cost.PerThousandTickets!.Value, 9);
        Assert.Equal(2, cost.Reported);
        Assert.Equal(0, cost.Priced);
        Assert.Equal(0, cost.Unknown);
    }

    [Fact]
    public void Cost_PrefersReportedCost_ThenPrice_ThenUnknown()
    {
        var records = new[]
        {
            Timed("1", 1, Timed("local", "Answered", 1, "local-m", 100, 10), Timed("typesafe", "Answered", 1, "zeta", 1000, 500, cost: 0.5)),
            Timed("2", 1, Timed("local", "Answered", 1, "local-m", 100, 10), Timed("typesafe", "Answered", 1, "zeta", 1000, 500)),
            Timed("3", 1, Timed("local", "Answered", 1, "local-m", 100, 10), Timed("typesafe", "Answered", 1, "mystery", 1000, 500)),
        };

        var cost = TriageReportBuilder.Build(records, Dataset("1", "2", "3"), Prices()).Cost;

        // Ticket 1 is 0.5 (its local call is zero-priced, so the ticket counts as priced); ticket 2 is 1000*1000/1e6 + 500*2000/1e6 = 2; ticket 3 has an unpriced call and is unknown.
        Assert.Equal(2.5 / 2 * 1000, cost.PerThousandTickets!.Value, 6);
        Assert.Equal(0, cost.Reported);
        Assert.Equal(2, cost.Priced);
        Assert.Equal(1, cost.Unknown);
    }

    [Fact]
    public void Cost_PricesUsed_ListsOnlyUsedModels_SortedOrdinal()
    {
        var records = new[]
        {
            Timed("1", 1, Timed("local", "Answered", 1, "zeta", 10, 1), Timed("typesafe", "Answered", 1, "alpha-2", 10, 1)),
        };

        var cost = TriageReportBuilder.Build(records, Dataset("1"), Prices()).Cost;

        Assert.Equal(["alpha", "zeta"], cost.PricesUsed.Select(p => p.Model).ToArray());
    }

    [Fact]
    public void Cost_DividesByTheCostedTickets_AndShowsTheUnknownCount()
    {
        var records = new[]
        {
            Timed("1", 1, Timed("local", "Answered", 1, cost: 0.004)),
            Timed("2", 1, Timed("local", "Answered", 1, "mystery", 10, 1)),
        };

        var cost = TriageReportBuilder.Build(records, Dataset("1", "2"), NoPrices).Cost;

        Assert.Equal(4.0, cost.PerThousandTickets!.Value, 9);
        Assert.Equal(1, cost.Unknown);
    }

    [Fact]
    public void Cost_IsNull_WhenNoTicketCouldBeCosted()
    {
        var records = new[] { Timed("1", 1, Timed("local", "Answered", 1, "mystery", 10, 1)) };

        var cost = TriageReportBuilder.Build(records, Dataset("1"), NoPrices).Cost;

        Assert.Null(cost.PerThousandTickets);
        Assert.Equal(1, cost.Unknown);
    }

    [Fact]
    public void Cost_AFailedCallWithAModelButNoUsage_MakesTheTicketUnknown()
    {
        var records = new[] { Timed("1", 1, Timed("local", "Answered", 1, cost: 0.001), Timed("typesafe", "Failed", 5, "zeta")) };

        var cost = TriageReportBuilder.Build(records, Dataset("1"), Prices()).Cost;

        Assert.Equal(1, cost.Unknown);
        Assert.Null(cost.PerThousandTickets);
    }
}
