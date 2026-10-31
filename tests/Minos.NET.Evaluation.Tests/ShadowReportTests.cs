using System.Globalization;
using static Minos.Evaluation.Tests.RecordLines;

namespace Minos.Evaluation.Tests;

// The shadow report on hand-built record files, read through the real reader: agreement, confusion, the items
// calibration counts, and the counts of each side.
public sealed class ShadowReportTests
{
    // One Choice question, for the tests that need no other.
    private const string QueueOnly =
        """{"queue":{"type":"choice","instructions":"Which queue should handle this?","criteria":{"billing":"Payments, invoices and refunds","technical":"Bugs, outages and integrations"}}}""";

    [Fact]
    public async Task Agreement_PerKey()
    {
        var report = await ReportAsync(
            Shadowed("d1", Answers(Choice("queue", 1)), Answers(Choice("queue", 1)), QueueOnly),
            Shadowed("d2", Answers(Choice("queue", 1)), Answers(Choice("queue", 1)), QueueOnly),
            Shadowed("d3", Answers(Choice("queue", 0)), Answers(Choice("queue", 0)), QueueOnly),
            Shadowed("d4", Answers(Choice("queue", 1)), Answers(Choice("queue", 0)), QueueOnly));

        var queue = Assert.Single(report.Questions);
        Assert.Equal("queue", queue.Key);
        Assert.Equal(QuestionKind.Choice, queue.Kind);
        Assert.Equal(4, queue.Compared);
        Assert.Equal(0.75, queue.Agreement);
        Assert.Null(queue.MeanLevelDifference);
        Assert.True(queue.IsSmall);
    }

    [Fact]
    public async Task Noul_AgreesAtTheHalfThreshold()
    {
        const string urgentOnly = """{"urgent":{"type":"noul","instructions":"Does this convey urgency?"}}""";

        var agreeing = await ReportAsync(Shadowed("d1", Answers(Noul("urgent", 0.5)), Answers(Noul("urgent", 0.9)), urgentOnly));
        var disagreeing = await ReportAsync(Shadowed("d1", Answers(Noul("urgent", 0.49)), Answers(Noul("urgent", 0.5)), urgentOnly));

        Assert.Equal(1.0, Assert.Single(agreeing.Questions).Agreement);
        Assert.Equal(0.0, Assert.Single(disagreeing.Questions).Agreement);
        Assert.Empty(Assert.Single(agreeing.Questions).Labels);
        Assert.Empty(Assert.Single(disagreeing.Questions).Confusion);
    }

    [Fact]
    public async Task Score_MeanLevelDifference()
    {
        const string fiveLevels = """{"mood":{"type":"score","instructions":"How upset is the customer?","criteria":["calm","mild","annoyed","angry","furious"]}}""";

        var report = await ReportAsync(
            Shadowed("d1", Answers(Score("mood", 1)), Answers(Score("mood", 1)), fiveLevels),
            Shadowed("d2", Answers(Score("mood", 1)), Answers(Score("mood", 3)), fiveLevels),
            Shadowed("d3", Answers(Score("mood", 2)), Answers(Score("mood", 3)), fiveLevels));

        var mood = Assert.Single(report.Questions);
        Assert.Equal(1.0, mood.MeanLevelDifference);
        Assert.Equal(1.0 / 3, mood.Agreement);
        Assert.Equal(["0", "1", "2", "3", "4"], mood.Labels);
    }

    [Fact]
    public async Task Confusion_IsSparse_AndLabelledByOptionKey()
    {
        var report = await ReportAsync(
            Shadowed("d1", Answers(Choice("queue", 0)), Answers(Choice("queue", 0)), QueueOnly),
            Shadowed("d2", Answers(Choice("queue", 0)), Answers(Choice("queue", 1)), QueueOnly),
            Shadowed("d3", Answers(Choice("queue", 0)), Answers(Choice("queue", 1)), QueueOnly));

        var queue = Assert.Single(report.Questions);
        Assert.Equal(["billing", "technical"], queue.Labels);
        Assert.Equal([new ConfusionCell("billing", "billing", 1), new ConfusionCell("billing", "technical", 2)], queue.Confusion);
    }

    [Fact]
    public async Task SameKeyDifferentOptions_AreSeparateGroups()
    {
        const string withSales =
            """{"queue":{"type":"choice","instructions":"Which queue should handle this?","criteria":{"billing":"Payments","sales":"New contracts"}}}""";

        var report = await ReportAsync(
            Shadowed("d1", Answers(Choice("queue", 1)), Answers(Choice("queue", 1)), QueueOnly),
            Shadowed("d2", Answers(Choice("queue", 1)), Answers(Choice("queue", 0)), withSales),
            Shadowed("d3", Answers(Choice("queue", 0)), Answers(Choice("queue", 0)), QueueOnly));

        Assert.Equal(2, report.Questions.Count);
        Assert.All(report.Questions, q => Assert.Equal("queue", q.Key));
        Assert.Equal(["billing", "technical"], report.Questions[0].Labels);
        Assert.Equal(2, report.Questions[0].Compared);
        Assert.Equal(["billing", "sales"], report.Questions[1].Labels);
        Assert.Equal(1, report.Questions[1].Compared);
        Assert.Equal([new ConfusionCell("sales", "billing", 1)], report.Questions[1].Confusion);
    }

    [Fact]
    public async Task PrimaryOnlyAndCandidateOnly_AreCounted()
    {
        var report = await ReportAsync(
            Shadowed("d1", All(), Shadow("[]", errorKind: "Refused", refusedKeys: ["queue"])),
            Shadowed("d2", Answers(Noul("urgent", 0.9), Choice("queue", 1)), All()));

        var urgent = Question(report, "urgent");
        var queue = Question(report, "queue");
        var mood = Question(report, "mood");
        Assert.Equal((1, 1, 0), (urgent.Compared, urgent.PrimaryOnly, urgent.CandidateOnly));
        Assert.Equal((1, 1, 0), (queue.Compared, queue.PrimaryOnly, queue.CandidateOnly));
        Assert.Equal((0, 1, 1), (mood.Compared, mood.PrimaryOnly, mood.CandidateOnly));
        Assert.Null(mood.Agreement);
        Assert.Equal(1, report.Candidate.Refusals);
        Assert.Equal(0, report.Primary.Refusals);
        Assert.Equal([new KeyValuePair<string, int>("Refused", 1)], report.Candidate.Errors.ByKind);
    }

    [Fact]
    public async Task Calibration_UsesTheSameItemsOnBothSides()
    {
        var report = await ReportAsync(
            Shadowed("d1", All(), All()),
            Confirmed("d1", "2026-10-22T09:35:00+00:00", "reviewer-ui", """{"queue":"technical"}"""),
            Shadowed("d2", All(), Answers(Noul("urgent", 0.9), Score("mood", 0))),
            Confirmed("d2", "2026-10-22T09:35:00+00:00", "reviewer-ui", """{"queue":"billing"}"""),
            Record("d3"),
            Confirmed("d3", "2026-10-22T09:35:00+00:00", "reviewer-ui", """{"queue":"technical"}"""));

        foreach (var side in new[] { report.Primary, report.Candidate })
        {
            Assert.Equal(1, side.CalibratedDecisions);
            Assert.Equal(1, side.CalibratedKeys);
            Assert.Equal(1, side.Choice!.Scored);
            Assert.Equal(1.0, side.Choice.Accuracy);
            Assert.Null(side.Noul);
            Assert.Null(side.Score);
            Assert.Equal(1, side.Overall.Scored);
        }
    }

    [Fact]
    public async Task Calibration_IncludesImageDecisions()
    {
        var report = await ReportAsync(
            Record("d1", images: 2, answers: All(), shadow: Shadow(All())),
            Confirmed("d1", "2026-10-22T09:35:00+00:00", "reviewer-ui", """{"urgent":true}"""));

        Assert.Equal(1, report.Primary.CalibratedKeys);
        Assert.Equal(1, report.Candidate.CalibratedKeys);
        Assert.Equal(1, report.Primary.Noul!.Answered);
    }

    [Fact]
    public async Task Calibration_UsesTheLatestConfirmedAnswer()
    {
        // billing is confirmed latest; technical comes later in the file but at an earlier time; sales is no option of
        // the question, so it is dropped before the merge though it is the latest of all.
        var report = await ReportAsync(
            Shadowed("d1", Answers(Choice("queue", 0)), Answers(Choice("queue", 1)), QueueOnly),
            Confirmed("d1", "2026-10-22T09:40:00+00:00", "reviewer-ui", """{"queue":"billing"}"""),
            Confirmed("d1", "2026-10-22T09:35:00+00:00", "crm", """{"queue":"technical"}"""),
            Confirmed("d1", "2026-10-22T09:50:00+00:00", "crm", """{"queue":"sales"}"""));

        Assert.Equal(1.0, report.Primary.Choice!.Accuracy);
        Assert.Equal(0.0, report.Candidate.Choice!.Accuracy);
        Assert.Equal(1, report.Primary.CalibratedKeys);
    }

    [Fact]
    public async Task UnshadowedDecisions_AreReadButNotCompared()
    {
        var report = await ReportAsync(Record("d1"), Shadowed("d2", All(), All()), Record("d3"));

        Assert.Equal(3, report.DecisionsRead);
        Assert.Equal(1, report.DecisionsShadowed);
        Assert.All(report.Questions, q => Assert.Equal(1, q.Compared));
        Assert.Equal(1, report.Primary.Latency.Requests);
        Assert.Equal(1, report.Candidate.Latency.Requests);
    }

    [Fact]
    public async Task CandidateErrors_AreSummarisedByKind()
    {
        var report = await ReportAsync(
            Shadowed("d1", All(), Shadow("[]", errorKind: "Timeout")),
            Shadowed("d2", All(), Shadow("[]", errorKind: "Server")),
            Shadowed("d3", All(), Shadow("[]", errorKind: "Timeout")),
            Shadowed("d4", All(), All()));

        Assert.Equal(3, report.Candidate.Errors.Count);
        Assert.Equal([new KeyValuePair<string, int>("Server", 1), new KeyValuePair<string, int>("Timeout", 2)], report.Candidate.Errors.ByKind);
        Assert.Equal(0, report.Primary.Errors.Count);
        Assert.Equal(1, report.Candidate.Latency.Requests);
        Assert.Equal(4, report.Primary.Latency.Requests);
        Assert.Equal(1, Question(report, "queue").Compared);
        Assert.Equal(3, Question(report, "queue").PrimaryOnly);
    }

    [Fact]
    public async Task Signals_SplitByAgreement()
    {
        // d1 agrees on every key; d2 agrees on urgent but not on queue, so all its signals count as disagreed, under
        // urgent too; d3 is not shadowed, so its signal is in neither.
        var report = await ReportAsync(
            Shadowed("d1", All(), All()),
            Signal("d1", "2026-10-22T09:35:00+00:00", "mailer", "opened"),
            Shadowed("d2", All(), Answers(Noul("urgent", 0.9), Choice("queue", 0), Score("mood", 0))),
            Signal("d2", "2026-10-22T09:35:00+00:00", "mailer", "opened"),
            Signal("d2", "2026-10-22T09:36:00+00:00", "mailer", "complained", 1),
            Record("d3"),
            Signal("d3", "2026-10-22T09:35:00+00:00", "mailer", "complained"));

        KeyValuePair<string, int>[] agreed = [new("opened", 1)];
        KeyValuePair<string, int>[] disagreed = [new("complained", 1), new("opened", 1)];
        foreach (var key in new[] { "urgent", "queue" })
        {
            Assert.Equal(agreed, Question(report, key).SignalsWhenAgreed);
            Assert.Equal(disagreed, Question(report, key).SignalsWhenDisagreed);
        }
    }

    [Fact]
    public async Task NoIdKeyOrState_InTheReport()
    {
        const string sentinel = "SENTINEL-7f3a";
        var report = await ReportAsync(
            Record("id-" + sentinel, state: "\"state " + sentinel + "\"", answers: All(), shadow: Shadow(All()), erasureKey: "key-" + sentinel),
            Confirmed("id-" + sentinel, "2026-10-22T09:35:00+00:00", "reviewer-ui", """{"queue":"technical"}"""),
            Signal("id-" + sentinel, "2026-10-22T09:35:00+00:00", "mailer", "opened", note: "note " + sentinel));

        Assert.Equal(1, report.DecisionsShadowed);
        Assert.DoesNotContain(sentinel, report.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_GivesTheSameReportAsCreate()
    {
        var decisions = await ReadAsync(Shadowed("d1", All(), All()), Confirmed("d1", "2026-10-22T09:35:00+00:00", "reviewer-ui", """{"urgent":true}"""));

        var viaAsync = await ShadowReport.CreateAsync(Yield(decisions));

        Assert.Equal(ShadowReport.Create(decisions).ToJson(), viaAsync.ToJson());
    }

    [Fact]
    public async Task Create_RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => ShadowReport.Create(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ShadowReport.CreateAsync(null!));
    }

    internal static async Task<ShadowReport> ReportAsync(params string[] lines) => ShadowReport.Create(await ReadAsync(lines));

    internal static ShadowQuestion Question(ShadowReport report, string key) => Assert.Single(report.Questions, q => string.Equals(q.Key, key, StringComparison.Ordinal));

    // A record of the default questions or of the given ones, with the primary's answers and the candidate's part:
    // either a list of answers, which becomes a successful shadow, or a whole shadow object.
    internal static string Shadowed(string id, string primary, string candidate, string questions = RecordLines.Questions)
        => Record(id, questions, answers: primary, shadow: candidate.StartsWith('[') ? Shadow(candidate) : candidate);

    // The answers of the default questions, all three.
    internal static string All() => Answers(Noul("urgent", 0.9), Choice("queue", 1), Score("mood", 0));

    internal static string Answers(params string[] answers) => "[" + string.Join(',', answers) + "]";

    internal static string Noul(string key, double p, string? source = null)
        => $$"""{"key":"{{key}}","kind":"noul","probability":{{R(p)}},"confidence":{{R(Math.Max(p, 1 - p))}}{{Source(source)}}}""";

    internal static string Choice(string key, int index, double confidence = 0.8, string? source = null)
        => $$"""{"key":"{{key}}","kind":"choice","chosenIndex":{{index}},"confidence":{{R(confidence)}}{{Source(source)}}}""";

    internal static string Score(string key, int level, double confidence = 0.7, string? source = null)
        => $$"""{"key":"{{key}}","kind":"score","chosenIndex":{{level}},"expected":{{level}},"confidence":{{R(confidence)}}{{Source(source)}}}""";

    internal static string Shadow(
        string answers, string? errorKind = null, string[]? refusedKeys = null, string model = "candidate-1", double latencyMs = 40, string? usage = null)
    {
        var shadow = "{\"provider\":\"candidate\",\"servedModel\":\"" + model + "\",\"latencyMs\":" + R(latencyMs);
        if (usage is not null)
        {
            shadow += ",\"usage\":" + usage;
        }

        if (errorKind is null)
        {
            return shadow + ",\"answers\":" + answers + "}";
        }

        shadow += ",\"errorKind\":\"" + errorKind + "\"";
        if (refusedKeys is not null)
        {
            shadow += ",\"refusedKeys\":[" + string.Join(',', refusedKeys.Select(k => "\"" + k + "\"")) + "]";
        }

        return shadow + "}";
    }

    private static string Source(string? source) => source is null ? string.Empty : ",\"probabilitySource\":\"" + source + "\"";

    private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
