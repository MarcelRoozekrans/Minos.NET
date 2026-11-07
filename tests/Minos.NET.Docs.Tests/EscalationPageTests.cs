using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// Keeps the Escalation category in its place in the sidebar. Later recipe pages add their own tests here.
public sealed partial class EscalationPageTests
{
    [Fact]
    public void TheCategory_SitsBeforePatterns()
    {
        Assert.Equal(18, CategoryPosition("escalation"));
        Assert.Equal(19, CategoryPosition("patterns"));
    }

    [Fact]
    public void TheIndex_IsPositionOne()
    {
        var front = PublishedPages.FrontMatter("docs/escalation/index.md");

        Assert.Equal("escalation", front["id"]);
        Assert.Equal("1", front["sidebar_position"]);
    }

    // Each figure is anchored on the words after it, so a bare number cannot match elsewhere on the page. The "wrong"
    // format is the questions the local model got wrong: its total less its correct answers.
    public static TheoryData<string, string, string> ThresholdFigures() => new()
    {
        { "signal.choice.wrongCaught", "integer", "31 of the 37 wrong Choice answers" },
        { "signal.choice.wrongMissed", "integer", "6 slipped through" },
        { "signal.noul.wrongCaught", "integer", "1 of the 51 wrong Noul answers" },
        { "signal.noul.wrongMissed", "integer", "50 slipped through" },
        { "split.noul.escalated", "integer", "5 of the 800 Noul questions" },
        { "accuracy.choice.localAlone.correct", "wrong", "37 of the 400 Choice questions" },
        { "accuracy.noul.localAlone.correct", "wrong", "51 of the 800 Noul questions" },
    };

    [Fact]
    public void TheDefaultThreshold_IsPointSix()
    {
        Assert.Equal(0.6, new EscalationOptions().DefaultThreshold);
        Assert.Contains("`DefaultThreshold` is 0.6", EscalationReport.PageText("thresholds"), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ThresholdFigures))]
    public void ThresholdFigures_AreTheReports(string path, string format, string expected)
    {
        var report = EscalationReport.Triage;
        var value = EscalationReport.Resolve(report, path).GetValue<int>();
        if (string.Equals(format, "wrong", StringComparison.Ordinal))
        {
            var total = EscalationReport.Resolve(report, path.Replace(".correct", ".total", StringComparison.Ordinal)).GetValue<int>();
            value = total - value;
        }

        var text = value.ToString(CultureInfo.InvariantCulture);
        Assert.True(expected.StartsWith(text + " ", StringComparison.Ordinal), $"The report's {path} gives {text}.");
        Assert.Contains(expected, EscalationReport.PageText("thresholds"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCalibrationRow_IsTheReports()
    {
        var row = EscalationReport.Calibration["choice"]!["sweep"]!.AsArray().Single(r => Math.Abs(r!["threshold"]!.GetValue<double>() - 0.85) < 1e-9)!;
        var coverage = row["coverage"]!.GetValue<double>().ToString("F4", CultureInfo.InvariantCulture);
        var accuracy = row["accuracy"]!.GetValue<double>().ToString("F4", CultureInfo.InvariantCulture);
        var covered = row["covered"]!.GetValue<int>().ToString(CultureInfo.InvariantCulture);
        var line = $"| 0.85 | {coverage} | {accuracy} | {covered} |";

        Assert.Equal("| 0.85 | 0.8200 | 0.9787 | 328 |", line);
        Assert.Contains(line, EscalationReport.PageText("thresholds"), StringComparison.Ordinal);
        Assert.Contains(line, File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", "reports", "local", "banking77", "report.md")), StringComparison.Ordinal);
        Assert.Contains("328 of the 400 Choice questions", EscalationReport.PageText("thresholds"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSweepEstimate_AndTheTriageCount_AreTheReports()
    {
        var sweep = EscalationReport.Calibration["choice"]!["sweep"]!.AsArray().Single(r => Math.Abs(r!["threshold"]!.GetValue<double>() - 0.85) < 1e-9)!;
        var below = 400 - sweep["covered"]!.GetValue<int>();
        var escalated = EscalationReport.Resolve(EscalationReport.Triage, "split.choice.escalated").GetValue<int>();
        var page = EscalationReport.PageText("thresholds");

        Assert.Equal(72, below);
        Assert.Equal(101, escalated);
        Assert.Contains("The other 72 questions", page, StringComparison.Ordinal);
        Assert.Contains("escalated 101 of the 400 Choice questions at 0.85, not 72", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheThresholdsInTheSnippet_AreTheReportsConfig()
    {
        var line = EscalationReport.TriageReadme.Split('\n').Single(l => l.StartsWith("Thresholds:", StringComparison.Ordinal));
        var fromReport = ReportThreshold().Matches(line).ToDictionary(m => m.Groups["key"].Value, m => double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture), StringComparer.Ordinal);
        var source = File.ReadAllText(Path.Combine(PublishedPages.Root, "tests", "Minos.NET.Docs.Tests", "EscalationThresholds.cs"));
        var fromSnippet = SnippetThreshold().Matches(source).ToDictionary(m => m.Groups["key"].Value, m => double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture), StringComparer.Ordinal);

        Assert.Equal(3, fromReport.Count);
        Assert.Equal(fromReport.OrderBy(p => p.Key, StringComparer.Ordinal), fromSnippet.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void TheThresholdsPage_LinksTheTwoReferences()
    {
        var page = EscalationReport.PageText("thresholds");

        Assert.Contains("(../patterns/confidence-routing.md#the-gates)", page, StringComparison.Ordinal);
        Assert.Contains("(../pipeline.md#escalating-unsure-questions)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetFigures_AreTheReports()
    {
        var report = EscalationReport.Triage;
        var page = EscalationReport.PageText("time-budget");

        Assert.Equal("5282.9", EscalationReport.Resolve(report, "latency.local.medianMs").GetValue<double>().ToString("F1", CultureInfo.InvariantCulture));
        Assert.Equal("102.3", EscalationReport.Resolve(report, "latency.target.medianMs").GetValue<double>().ToString("F1", CultureInfo.InvariantCulture));
        Assert.Equal(106, EscalationReport.Resolve(report, "split.all.escalated").GetValue<int>());
        Assert.Equal(104, EscalationReport.Resolve(report, "split.all.ticketsWithTargetCall").GetValue<int>());
        Assert.Contains("a median of 5282.9 ms", page, StringComparison.Ordinal);
        Assert.Contains("a median of 102.3 ms", page, StringComparison.Ordinal);
        Assert.Contains("106 questions", page, StringComparison.Ordinal);
        Assert.Contains("104 target calls", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("5.5 s")]
    [InlineData("6.1 s")]
    [InlineData("106 questions")]
    [InlineData("no target call")]
    public void TheFirstRun_IsQuotedFromTheReadme(string phrase)
    {
        Assert.Contains(phrase, EscalationReport.PageText("time-budget"), StringComparison.Ordinal);

        // The README wraps its lines, so a phrase may break across two of them.
        var readme = string.Join(' ', EscalationReport.TriageReadme.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(phrase, readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportsBudgetAndExpectedLatency_AreOnThePage()
    {
        var budget = ReadmeLine("Budget:");
        var expected = ReadmeLine("Expected latency:");
        var page = EscalationReport.PageText("time-budget");
        var snippet = File.ReadAllText(Path.Combine(PublishedPages.Root, "tests", "Minos.NET.Docs.Tests", "EscalationBudget.cs"));

        Assert.StartsWith("15 seconds", budget, StringComparison.Ordinal);
        Assert.Equal("300 ms", expected);
        Assert.Contains("a budget of 15 seconds and an expected latency of 300 ms", page, StringComparison.Ordinal);
        Assert.Contains("Budget = TimeSpan.FromSeconds(15)", snippet, StringComparison.Ordinal);
        Assert.Contains("ExpectedLatency = TimeSpan.FromMilliseconds(300)", snippet, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DecisionErrorKind.Network)]
    [InlineData(DecisionErrorKind.Validation)]
    public void TheDefaultShouldFallBack_IsProviderFailure(DecisionErrorKind kind)
    {
        var error = new DecisionError(kind, "failed");

        Assert.Equal(FallbackOptions.IsProviderFailure(error), new FallbackOptions().ShouldFallBack(error));
        Assert.Equal(kind == DecisionErrorKind.Network, FallbackOptions.IsProviderFailure(error));
    }

    [Fact]
    public void TheCallStatuses_AreTheNamesOnThePage()
    {
        var page = EscalationReport.PageText("fallback");

        foreach (var name in Enum.GetNames<CallStatus>())
        {
            Assert.Contains($"`{name}`", page, StringComparison.Ordinal);
        }

        Assert.Contains($"`{nameof(RouteReason.FellBack)}`", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFallbackPage_LinksThePipelineSection()
    {
        Assert.Contains("(../pipeline.md#falling-back-to-another-provider)", EscalationReport.PageText("fallback"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBudgetPage_LinksThePipelineAndRecording()
    {
        var page = EscalationReport.PageText("time-budget");

        Assert.Contains("(../pipeline.md#escalating-unsure-questions)", page, StringComparison.Ordinal);
        Assert.Contains("(../recording.md)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnippet_PutsEscalationOutsideFallback()
    {
        var source = File.ReadAllText(Path.Combine(PublishedPages.Root, "tests", "Minos.NET.Docs.Tests", "EscalationCompose.cs"));

        // The first Use is the outermost stage.
        Assert.True(source.IndexOf(".UseRecording(", StringComparison.Ordinal) < source.IndexOf(".UseEscalation(", StringComparison.Ordinal));
        Assert.True(source.IndexOf(".UseEscalation(", StringComparison.Ordinal) < source.IndexOf(".UseFallback(", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePageOrder_IsThePipelinePagesOrder()
    {
        var pipeline = File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", "pipeline.md"));
        var section = pipeline[pipeline.IndexOf("### Stage order", StringComparison.Ordinal)..];
        var fromPipeline = StageWords(section);

        var page = EscalationReport.PageText("composing-and-privacy");
        var list = page[page.IndexOf("1. recording", StringComparison.Ordinal)..];
        var fromPage = StageWords(list);

        Assert.Equal(["recording", "telemetry", "escalation", "fallback", "caching", "retries", "rate limiting"], fromPipeline);
        Assert.Equal(fromPipeline, fromPage);
        Assert.Contains("(../pipeline.md#building-your-own-pipeline)", page, StringComparison.Ordinal);
        Assert.Contains("(../running-locally.md#what-stays-private-step-by-step)", page, StringComparison.Ordinal);
        Assert.Contains("(../recording.md#the-escalation-path)", page, StringComparison.Ordinal);
        Assert.Contains("(../recording.md#erasure)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFallbackPage_SaysWhenTheLastEntryAnswers()
    {
        var page = EscalationReport.PageText("fallback");

        var text = string.Join(' ', page.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("when the answering step has no routing stage of its own", text, StringComparison.Ordinal);
        Assert.Contains("keeps its routes: a question it escalated still reads `Escalated`, and its calls follow the failed ones", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Model")]
    [InlineData("Logprobs")]
    [InlineData("Verbalized")]
    public void ThePage_NamesEachProbabilitySource(string source)
    {
        Assert.True(Enum.IsDefined(typeof(ProbabilitySource), source));
        Assert.Contains($"`{source}`", EscalationReport.PageText("chat-model-target"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTargetSnippet_ReadsTheAnswersSource()
    {
        var source = File.ReadAllText(Path.Combine(PublishedPages.Root, "tests", "Minos.NET.Docs.Tests", "EscalationChatTarget.cs"));

        Assert.Contains("new ChatDecisionClient(chat)", source, StringComparison.Ordinal);
        Assert.Contains("result.Value.ProbabilitySource", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChatTargetPage_LinksTheChatClientsGuide()
    {
        var page = EscalationReport.PageText("chat-model-target");

        Assert.Contains("(../chat-clients.md#probability-modes)", page, StringComparison.Ordinal);
        Assert.Contains("(../chat-clients.md#where-the-probabilities-came-from)", page, StringComparison.Ordinal);
        Assert.Contains("(../chat-clients.md#reasoning-models)", page, StringComparison.Ordinal);
    }

    // The names of the numbered stages in a list, in the order they appear: the text between "N. " and the first comma.
    private static List<string> StageWords(string text) =>
        text.Split('\n')
            .Select(line => ListItem().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .Take(7)
            .ToList();

    [GeneratedRegex(@"^\d\. (?<name>[a-z ]+?)(?:,| \(|;|$)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ListItem();

    private static string ReadmeLine(string prefix) =>
        EscalationReport.TriageReadme.Split('\n').Single(l => l.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].Trim().Split(',')[0];

    private static int CategoryPosition(string folder) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", folder, "_category_.json")))!["position"]!.GetValue<int>();

    // "intent (Choice) 0.85, card_not_arrived 0.80, about_a_payment 0.80": the key, then its value.
    [GeneratedRegex(@"(?<key>\w+)(?: \(\w+\))? (?<value>\d\.\d+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReportThreshold();

    // options.Thresholds["intent"] = 0.85;
    [GeneratedRegex(@"Thresholds\[""(?<key>\w+)""\] = (?<value>\d\.\d+);", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnippetThreshold();
}
