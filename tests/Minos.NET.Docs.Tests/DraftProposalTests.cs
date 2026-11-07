using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The dotnet/extensions proposal draft quotes the committed Banking77 reports and the compiled Escalation snippets.
public sealed partial class DraftProposalTests
{
    private const string Draft = "docs/plans/2026-11-06-dotnet-extensions-decision-proposal-draft.md";

    private static readonly JsonNode Shadow =
        JsonNode.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, "docs/reports/shadow/banking77/shadow-report.json")))!;

    private static string DraftText => File.ReadAllText(Path.Combine(PublishedPages.Root, Draft));

    public static TheoryData<string, string, string> TriageFigures() => new()
    {
        { "split.all.confident", "{0:N0} of 1,200", "| Questions answered by the local model and kept | {0} |" },
        { "split.all.escalated", "{0} of 1,200", "| Questions escalated | {0}, in 104 target calls" },
        { "split.all.ticketsWithTargetCall", "{0}", "in {0} target calls" },
        { "accuracy.all.localAlone.rate", "{0:F4}", "the 1,200 questions | {0} |" },
        { "accuracy.all.final.rate", "{0:F4}", "| Accuracy of the final answer, over the 1,200 questions | {0} |" },
        { "escalated.all.fixed", "{0}", "| {0} fixed, " },
        { "escalated.all.broken", "{0}", " fixed, {0} broken |" },
        { "latency.local.medianMs", "{0:F1}", "| {0} ms / " },
        { "latency.target.medianMs", "{0:F1}", " ms / {0} ms |" },
        { "cost.perThousandTickets", "{0}", "| {0} |" },
        { "signal.choice.wrongCaught", "{0}", "it caught {0} of the 37 wrong Choice answers" },
        { "signal.noul.wrongCaught", "{0}", "and {0} of the 51 wrong yes/no answers" },
        { "run.decided", "{0}", "set of {0} tickets" },
    };

    public static TheoryData<string, string, string> ShadowFigures() => new()
    {
        { "primary.latency.medianMs", "{0:F1}", "| Median latency per request | {0} ms | " },
        { "candidate.latency.medianMs", "{0:F1}", " ms | {0} ms |" },
        { "primary.overall.ece", "{0:F4}", "over the 1,200 questions | {0} | " },
        { "candidate.overall.ece", "{0:F4}", "| {0} |" },
        { "questions[key=intent].agreement", "{0:F4}", "agreed on {0} of the 400 tickets" },
    };

    [Theory]
    [MemberData(nameof(TriageFigures))]
    public void TriageFigures_AreTheReports(string path, string format, string phrase) =>
        AssertQuoted(EscalationReport.Resolve(EscalationReport.Triage, path), format, phrase);

    [Theory]
    [MemberData(nameof(ShadowFigures))]
    public void ShadowFigures_AreTheReports(string path, string format, string phrase) =>
        AssertQuoted(EscalationReport.Resolve(Shadow, path), format, phrase);

    [Theory]
    [InlineData("median of 5.5 s")]
    [InlineData("all 106 questions below their")]
    [InlineData("nothing was billed")]
    public void TheFirstRun_IsQuotedFromTheReadme(string phrase)
    {
        Assert.Contains(phrase, EscalationReport.TriageReadme.ReplaceLineEndings(" "), StringComparison.Ordinal);
        Assert.Contains(phrase, DraftText.ReplaceLineEndings(" "), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EscalationThresholds.cs", "Escalation_Thresholds")]
    [InlineData("EscalationChatTarget.cs", "Escalation_ChatTarget")]
    public void TheUsageCode_IsACompiledSnippet(string file, string region)
    {
        var source = File.ReadAllText(Path.Combine(PublishedPages.Root, "tests/Minos.NET.Docs.Tests", file)).ReplaceLineEndings("\n");
        var body = Regex.Match(source, $@"#region {region}\n(?<body>.*?)\n\s*#endregion", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Groups["body"].Value;
        Assert.NotEmpty(body);
        var lines = body.Split('\n');
        var indent = lines.Where(line => line.Trim().Length > 0).Min(line => line.Length - line.TrimStart().Length);
        var snippet = string.Join('\n', lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
        Assert.Contains(snippet, DraftText.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static void AssertQuoted(JsonNode node, string format, string phrase)
    {
        var value = node.GetValue<double>();
        var text = string.Format(CultureInfo.InvariantCulture, format, value);
        Assert.Contains(string.Format(CultureInfo.InvariantCulture, phrase, text), DraftText.ReplaceLineEndings(" "), StringComparison.Ordinal);
    }
}
