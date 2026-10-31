using System.Globalization;
using System.Text;
using System.Text.Json;
using ZeroAlloc.TestHelpers;
using static Minos.Evaluation.Tests.RecordLines;
using static Minos.Evaluation.Tests.ShadowReportTests;

namespace Minos.Evaluation.Tests;

public sealed class ShadowReportOutputTests
{
    // A Noul, a 3-option Choice and a 5-level Score.
    private const string ThreeKinds =
        """{"urgent":{"type":"noul","instructions":"Does this convey urgency?"},"queue":{"type":"choice","instructions":"Which queue should handle this?","criteria":{"billing":"Payments","technical":"Bugs","sales":"New contracts"}},"mood":{"type":"score","instructions":"How upset is the customer?","criteria":["calm","mild","annoyed","angry","furious"]}}""";

    private const string At = "2026-10-22T09:35:00+00:00";

    private static readonly PriceTable Prices =
        new([new ModelPrice { Model = "candidate-1", InputPerMillion = 2, OutputPerMillion = 10, Date = new DateOnly(2026, 10, 30), Source = "provider listing" }]);

    private const string Usage = """{"inputTokens":1000,"outputTokens":100}""";

    private static async Task<ShadowReport> FixtureAsync()
    {
        var decisions = await ReadAsync(
            Shadowed(
                "d1",
                Answers(Noul("urgent", 0.9), Choice("queue", 1, 0.8), Score("mood", 2, 0.6)),
                Shadow(Answers(Noul("urgent", 0.8), Choice("queue", 1, 0.9), Score("mood", 2, 0.7)), latencyMs: 600, usage: Usage),
                ThreeKinds),
            Confirmed("d1", At, "reviewer-ui", """{"urgent":true,"queue":"technical","mood":2}"""),
            Signal("d1", At, "mailer", "opened"),
            Shadowed(
                "d2",
                Answers(Noul("urgent", 0.3), Choice("queue", 0, 0.7), Score("mood", 1, 0.5)),
                Shadow(Answers(Noul("urgent", 0.6, "verbalized"), Choice("queue", 2, 0.6, "verbalized"), Score("mood", 3, 0.5, "verbalized")), latencyMs: 900, usage: Usage),
                ThreeKinds),
            Confirmed("d2", At, "reviewer-ui", """{"urgent":false,"queue":"billing"}"""),
            Signal("d2", At, "mailer", "complained", 1),
            Shadowed(
                "d3",
                Answers(Noul("urgent", 0.95), Choice("queue", 2, 0.9), Score("mood", 4, 0.8)),
                Shadow(Answers(Noul("urgent", 0.7), Choice("queue", 2, 0.85), Score("mood", 3, 0.6)), latencyMs: 700, usage: Usage),
                ThreeKinds),
            Shadowed("d4", Answers(Noul("urgent", 0.1), Choice("queue", 0, 0.95)), Shadow("[]", errorKind: "Timeout", latencyMs: 30000), ThreeKinds),
            Confirmed("d4", At, "reviewer-ui", """{"urgent":false}"""),
            Record("d5", ThreeKinds, answers: Answers(Noul("urgent", 0.9), Choice("queue", 1), Score("mood", 0))));
        return ShadowReport.Create(decisions, Prices);
    }

    [Fact]
    public async Task Fixture_CountsTheConfirmedKeyOnlyThePrimaryAnswered()
    {
        // d4's confirmed urgent: the candidate timed out, so only the primary answered it.
        var report = await FixtureAsync();

        Assert.Equal(1, report.CalibrationPrimaryOnlyKeys);
        Assert.Equal(0, report.CalibrationCandidateOnlyKeys);
        Assert.Equal(2, report.Candidate.VerbalizedAnswers);
    }

    [Fact]
    public async Task Json_MatchesTheSnapshot() => TextSnapshot.VerifyText((await FixtureAsync()).ToJson(), "json");

    [Fact]
    public async Task Markdown_MatchesTheSnapshot() => TextSnapshot.VerifyText((await FixtureAsync()).ToMarkdown(), "md");

    [Fact]
    public async Task Json_HasKindVersionAndUsesLineFeedsOnly()
    {
        var json = (await FixtureAsync()).ToJson();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("minos-shadow-report", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.DoesNotContain('\r', json);
        Assert.DoesNotContain('\r', (await FixtureAsync()).ToMarkdown());
    }

    [Fact]
    public async Task LargeChoice_MarkdownListsTopDisagreements()
    {
        var options = new StringBuilder();
        for (var i = 0; i < 77; i++)
        {
            options.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture, $"\"{Label(i)}\":\"Intent {i}\"");
        }

        var questions = "{\"intent\":{\"type\":\"choice\",\"instructions\":\"Which intent?\",\"criteria\":{" + options + "}}}";

        // 25 distinct disagreements, the first five twice, one more o00 disagreement twice to tie with o00 to o30 on the
        // primary and the count, and 40 agreements spread over 40 options.
        var lines = new List<string>();
        var n = 0;
        for (var i = 0; i < 25; i++)
        {
            for (var copy = 0; copy < (i < 5 ? 2 : 1); copy++)
            {
                lines.Add(Shadowed("d" + n++, Answers(Choice("intent", i)), Answers(Choice("intent", i + 30)), questions));
            }
        }

        for (var copy = 0; copy < 2; copy++)
        {
            lines.Add(Shadowed("d" + n++, Answers(Choice("intent", 0)), Answers(Choice("intent", 31)), questions));
        }

        for (var i = 0; i < 40; i++)
        {
            lines.Add(Shadowed("d" + n++, Answers(Choice("intent", i)), Answers(Choice("intent", i)), questions));
        }

        var report = await ReportAsync([.. lines]);
        var markdown = report.ToMarkdown();

        Assert.DoesNotContain(markdown.Split('\n'), line => line.Count(c => c == '|') > 10);
        var rows = markdown.Split('\n').Where(line => line.StartsWith("| " + "o", StringComparison.Ordinal)).ToList();
        Assert.Equal(20, rows.Count);
        Assert.Equal("| o00 | o30 | 2 |", rows[0]);
        Assert.Equal("| o00 | o31 | 2 |", rows[1]);
        Assert.Equal("| o01 | o31 | 2 |", rows[2]);
        Assert.Equal("| o04 | o34 | 2 |", rows[5]);
        Assert.Equal("| o05 | o35 | 1 |", rows[6]);
        Assert.Equal("| o18 | o48 | 1 |", rows[19]);
        Assert.False(report.Questions[0].IsSmall);
        Assert.DoesNotContain("(small: n < 30)", markdown, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(report.ToJson());
        var cells = doc.RootElement.GetProperty("questions")[0].GetProperty("confusion");
        Assert.Equal(66, cells.GetArrayLength());
        Assert.All(cells.EnumerateArray(), cell => Assert.True(cell.GetProperty("count").GetInt32() > 0));
        Assert.Equal(66, report.Questions[0].Confusion.Count);
    }

    private static string Label(int i) => "o" + i.ToString("00", CultureInfo.InvariantCulture);
}
