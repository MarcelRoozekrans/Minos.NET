using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// Keeps the running-locally guide in step with the code and with the committed Banking77 shadow report: its two
// configuration blocks are the example app's, every report figure it quotes is the report's, and its commands are the
// ones CI and the reference pages use.
public sealed partial class RunningLocallyPageTests
{
    private const string Page = "running-locally.md";

    private const string ReportFolder = "docs/reports/shadow/banking77";

    private static readonly string[] ShadowFlags = ["--records", "--prices", "--out"];

    public static TheoryData<string, string, string> Figures() => new()
    {
        { "primary.cost.perThousandDecisions", "F7", "0.0293118" },
        { "primary.latency.medianMs", "F1", "50.5" },
        { "candidate.latency.medianMs", "F1", "4043.8" },
        { "candidate.latency.p95Ms", "F1", "4509.7" },
        { "primary.overall.accuracy", "F4", "0.9275" },
        { "candidate.overall.accuracy", "F4", "0.9267" },
        { "primary.noul.accuracy", "F4", "0.9287" },
        { "candidate.noul.accuracy", "F4", "0.9363" },
        { "primary.choice.accuracy", "F4", "0.9250" },
        { "candidate.choice.accuracy", "F4", "0.9075" },
        { "primary.overall.ece", "F4", "0.0262" },
        { "candidate.overall.ece", "F4", "0.0510" },
        { "questions[key=intent].agreement", "F4", "0.9600" },
        { "questions[key=card_not_arrived].agreement", "F4", "0.9300" },
        { "questions[key=about_a_payment].agreement", "F4", "0.9450" },
        { "primary.overall.scored", "integer", "1200" },
        { "decisionsShadowed", "integer", "400" },
    };

    [Fact]
    public void TheConfigBlocks_AreTheAppsConfig()
    {
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RunningLocallyConfig.Before), JsonNode.Parse(JsonBlock(0))), "The page's first json block is RunningLocallyConfig.Before.");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RunningLocallyConfig.After), JsonNode.Parse(JsonBlock(1))), "The page's second json block is RunningLocallyConfig.After.");
    }

    [Theory]
    [MemberData(nameof(Figures))]
    public void EveryReportFigure_IsOnThePage(string path, string format, string expected)
    {
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, ReportFolder, "shadow-report.json")))!;
        var value = Resolve(report, path);
        var text = string.Equals(format, "integer", StringComparison.Ordinal)
            ? value.GetValue<int>().ToString(CultureInfo.InvariantCulture)
            : value.GetValue<double>().ToString(format, CultureInfo.InvariantCulture);

        Assert.Equal(expected, text);
        Assert.True(PageText().Contains(text, StringComparison.Ordinal), $"The page shows {text}, the report's {path}.");
        var markdown = File.ReadAllText(Path.Combine(PublishedPages.Root, ReportFolder, "shadow-report.md"));
        Assert.True(markdown.Contains(text, StringComparison.Ordinal), $"shadow-report.md shows {text}, the report's {path}.");
    }

    [Fact]
    public void TheLocalServerCommand_IsTheOneCiRuns()
    {
        const string command = "ollajev serve Mapika/decider-2b-GGUF:Q4_K_M --port 8000 --no-browser";
        var workflow = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "local-systemone.yml"));

        Assert.Contains(command, PageText(), StringComparison.Ordinal);
        Assert.Contains(command, workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommands_UseTheToolsFlags()
    {
        var shadow = CommandLine(PageText(), "minos shadow-report");
        var export = CommandLine(PageText(), "minos export");
        var datasetsExport = CommandLine(File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", "datasets.md")), "minos export");

        Assert.All(ShadowFlags, flag => Assert.Contains(flag, shadow, StringComparison.Ordinal));
        Assert.Equal(Flags(datasetsExport), Flags(export));
    }

    [Fact]
    public void ThePage_NamesNoProject() => Assert.DoesNotContain("Daedalus", PageText(), StringComparison.OrdinalIgnoreCase);

    private static string PageText() => File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", Page));

    // The first line that starts with the command, outside of prose.
    private static string CommandLine(string text, string command)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith(command + " ", StringComparison.Ordinal));
        Assert.True(line is not null, $"A line starts with '{command}'.");
        return line!;
    }

    private static string[] Flags(string line) => [.. FlagName().Matches(line).Select(m => m.Value).Order(StringComparer.Ordinal)];

    // A path such as primary.latency.medianMs or questions[key=intent].agreement.
    private static JsonNode Resolve(JsonNode root, string path)
    {
        var node = root;
        foreach (var part in path.Split('.'))
        {
            var select = Selector().Match(part);
            node = select.Success ? WithKey(node[select.Groups["name"].Value]!.AsArray(), select.Groups["key"].Value) : node[part];
            Assert.NotNull(node);
        }

        return node!;
    }

    private static JsonNode WithKey(JsonArray items, string key)
    {
        foreach (var item in items)
        {
            if (string.Equals((string?)item!["key"], key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        throw new InvalidOperationException($"No item has the key {key}.");
    }

    // The body of the page's json code block number index, counting from 0.
    private static string JsonBlock(int index)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", Page));
        var start = -1;
        for (var found = 0; found <= index; found++)
        {
            start = Array.FindIndex(lines, start + 1, line => string.Equals(line, "```json", StringComparison.Ordinal));
            Assert.True(start >= 0, $"{Page} has {index + 1} json blocks.");
        }

        var end = Array.FindIndex(lines, start + 1, line => string.Equals(line, "```", StringComparison.Ordinal));
        return string.Join('\n', lines[(start + 1)..end]);
    }

    [GeneratedRegex(@"^(?<name>\w+)\[key=(?<key>\w+)\]$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Selector();

    [GeneratedRegex(@"--[a-z-]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FlagName();
}
