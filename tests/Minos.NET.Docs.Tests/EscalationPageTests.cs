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

    private static int CategoryPosition(string folder) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", folder, "_category_.json")))!["position"]!.GetValue<int>();

    // "intent (Choice) 0.85, card_not_arrived 0.80, about_a_payment 0.80": the key, then its value.
    [GeneratedRegex(@"(?<key>\w+)(?: \(\w+\))? (?<value>\d\.\d+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReportThreshold();

    // options.Thresholds["intent"] = 0.85;
    [GeneratedRegex(@"Thresholds\[""(?<key>\w+)""\] = (?<value>\d\.\d+);", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnippetThreshold();
}
