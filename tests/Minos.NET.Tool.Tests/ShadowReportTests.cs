using System.Text.Json.Nodes;
using static Minos.Tool.Tests.Harness;

namespace Minos.Tool.Tests;

[Collection(ProcessEnvironment.Name)]
public sealed class ShadowReportTests : IDisposable
{
    private const string Questions =
        "{\"urgent\":{\"type\":\"noul\",\"instructions\":\"Does this convey urgency?\"}}";

    private const string PrimaryAnswers =
        "[{\"key\":\"urgent\",\"kind\":\"noul\",\"probability\":0.9,\"confidence\":0.8}]";

    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    private string JsonFile => Path.Combine(_h.Out, "shadow-report.json");

    private string MarkdownFile => Path.Combine(_h.Out, "shadow-report.md");

    private static string Record(string id, string state = "\"The app crashes.\"", string? shadow = null)
        => "{\"kind\":\"record\",\"version\":1,\"decisionId\":\"" + id + "\",\"started\":\"2026-10-22T09:30:00+00:00\",\"latencyMs\":212.5,\"provider\":\"typesafe\","
            + "\"endpoint\":\"https://api.example.com/\",\"defaultModel\":\"jev-latest\",\"cached\":false,\"questions\":" + Questions
            + ",\"state\":" + state + ",\"imageCount\":0,\"answers\":" + PrimaryAnswers
            + (shadow is null ? string.Empty : ",\"shadow\":" + shadow) + "}";

    private static string Shadowed(string id, string state = "\"The app crashes.\"", double probability = 0.7)
        => Record(
            id,
            state,
            "{\"provider\":\"candidate\",\"servedModel\":\"candidate-1\",\"latencyMs\":40,\"usage\":{\"inputTokens\":1000,\"outputTokens\":100},"
            + "\"answers\":[{\"key\":\"urgent\",\"kind\":\"noul\",\"probability\":" + probability.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"confidence\":0.7}]}");

    private static string Confirmed(string id)
        => "{\"kind\":\"outcome\",\"version\":1,\"decisionId\":\"" + id + "\",\"type\":\"confirmed\",\"source\":\"agent\",\"at\":\"2026-10-23T09:30:00+00:00\",\"confirmed\":{\"urgent\":true}}";

    private string Records(params string[] lines) => _h.Write("decisions.jsonl", string.Join('\n', lines) + "\n");

    [Fact]
    public async Task ShadowReport_WritesBothFiles_ExitsZero()
    {
        var records = Records(Shadowed("a"), Confirmed("a"), Shadowed("b"), Record("c"));

        var run = await Invoke(null, "shadow-report", "--records", records, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        Assert.True(File.Exists(JsonFile));
        Assert.True(File.Exists(MarkdownFile));
        Assert.EndsWith(JsonFile + "\n" + MarkdownFile + "\n", run.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("decisions read: 3", run.Stdout, StringComparison.Ordinal);
        Assert.Contains("decisions shadowed: 2", run.Stdout, StringComparison.Ordinal);
        Assert.Contains("compared keys: 2", run.Stdout, StringComparison.Ordinal);
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(JsonFile)));
    }

    [Fact]
    public async Task ShadowReport_NoShadowedDecisions_ExitsTwo_WritesNothing()
    {
        var records = Records(Record("a"), Confirmed("a"), Record("b"));

        var run = await Invoke(null, "shadow-report", "--records", records, "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.False(Directory.Exists(_h.Out));
        Assert.Equal("", run.Stdout);
    }

    [Fact]
    public async Task ShadowReport_MissingFile_ExitsTwo()
    {
        var run = await Invoke(null, "shadow-report", "--records", Path.Combine(_h.Dir, "nope.jsonl"), "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.Contains("does not exist", run.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_h.Out));
    }

    [Fact]
    public async Task ShadowReport_CutLastLine_ReportsTheProblem_AndTheRest()
    {
        var records = _h.Write("cut.jsonl", Shadowed("a") + "\n" + Confirmed("a") + "\n" + "{\"kind\":\"outcome\",\"version\":1,\"deci");

        var run = await Invoke(null, "shadow-report", "--records", records, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        Assert.Contains("line 3", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("decisions shadowed: 1", run.Stdout, StringComparison.Ordinal);
        Assert.True(File.Exists(JsonFile));
    }

    [Fact]
    public async Task ShadowReport_WithPrices_PricesBothSides()
    {
        var records = Records(Shadowed("a"));
        var plain = await Invoke(null, "shadow-report", "--records", records, "--out", _h.Out);
        Assert.Equal(0, plain.Exit);
        var withoutPrices = File.ReadAllText(JsonFile);
        var prices = _h.Write(
            "prices.json",
            "{\"prices\":[{\"model\":\"candidate-1\",\"inputPerMillion\":2,\"outputPerMillion\":10,\"date\":\"2026-10-30\",\"source\":\"provider listing\"}]}");

        var run = await Invoke(null, "shadow-report", "--records", records, "--prices", prices, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        var withPrices = File.ReadAllText(JsonFile);
        Assert.NotEqual(withoutPrices, withPrices);
        Assert.Contains("provider listing", withPrices, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShadowReport_BadPricesFile_ExitsTwo()
    {
        var records = Records(Shadowed("a"));
        var prices = _h.Write("prices.json", "{\"prices\":[{\"model\":\"\"}]}");

        var run = await Invoke(null, "shadow-report", "--records", records, "--prices", prices, "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.False(Directory.Exists(_h.Out));
    }

    [Fact]
    public async Task ShadowReport_WriteFails_ExitsOne_DeletesPartials()
    {
        var records = Records(Shadowed("a"));

        // The markdown path is a directory, so the json file is written and writing the markdown then throws.
        Directory.CreateDirectory(MarkdownFile);
        var run = await Invoke(null, "shadow-report", "--records", records, "--out", _h.Out);

        Assert.Equal(1, run.Exit);
        Assert.False(File.Exists(JsonFile));
        Assert.Contains("The output cannot be written", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShadowReport_OutputHoldsNoSentinel()
    {
        var records = _h.Write(
            "sentinel.jsonl",
            string.Join('\n', (string[])[Shadowed("SENTINEL-ID", "\"SENTINEL-STATE\""), Confirmed("SENTINEL-ID"), Confirmed("SENTINEL-ORPHAN")])
                + "\n{\"kind\":\"record\",\"version\":1,\"decisionId\":\"SENTINEL-CUT\",\"state\":\"SENTINEL");

        var run = await Invoke(null, "shadow-report", "--records", records, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        Assert.Contains("line 3", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("line 4", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", File.ReadAllText(JsonFile), StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", File.ReadAllText(MarkdownFile), StringComparison.Ordinal);
    }
}
