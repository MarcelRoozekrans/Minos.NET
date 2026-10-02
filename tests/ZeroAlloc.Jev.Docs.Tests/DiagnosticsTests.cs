using System.Globalization;
using System.Text.RegularExpressions;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed partial class DiagnosticsTests
{
    private const string Page = "diagnostics.md";

    private static string Source(params string[] path) => File.ReadAllText(Path.Combine([PublishedPages.Root, .. path]));

    // Each rule: the id from DiagnosticIds.cs, and the title and severity from its descriptor in Diagnostics.cs.
    private static (string Id, string Severity, string Title)[] Descriptors()
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in IdConstant().Matches(Source("src", "ZeroAlloc.Jev.Generator", "DiagnosticIds.cs")))
        {
            ids[match.Groups["name"].Value] = match.Groups["id"].Value;
        }

        var rules = new List<(string, string, string)>();
        foreach (Match match in Descriptor().Matches(Source("src", "ZeroAlloc.Jev.Analyzers", "Diagnostics.cs")))
        {
            rules.Add((ids[match.Groups["name"].Value], match.Groups["severity"].Value, match.Groups["title"].Value));
        }

        return [.. rules.OrderBy(rule => rule.Item1, StringComparer.Ordinal)];
    }

    [Fact]
    public void TheRuleTableOnThePage_IsTheAnalyzersDescriptors()
    {
        var rows = PageTables.Rows(Page, "The rules");

        Assert.Equal(15, Descriptors().Length);
        Assert.Equal(
            Descriptors().Select(rule => $"{rule.Id} {rule.Severity} {rule.Title}"),
            rows.Select(row => $"{row[0]} {row[1]} {row[2]}"));
    }

    // The release-tracking file is a second record of the same ids and severities, checked by the build's own analyzer.
    [Fact]
    public void TheRuleTableOnThePage_IsTheShippedAnalyzerReleaseToo()
    {
        var rows = PageTables.Rows(Page, "The rules");
        var shipped = new List<string>();
        foreach (Match match in ShippedRule().Matches(Source("src", "ZeroAlloc.Jev.Analyzers", "AnalyzerReleases.Shipped.md")))
        {
            shipped.Add($"{match.Groups["id"].Value} {match.Groups["severity"].Value}");
        }

        Assert.Equal(shipped, rows.Select(row => $"{row[0]} {row[1]}"));
    }

    [Fact]
    public void TheGroups_AreTheSixApiRules_AndNineGeneratorRules_AndTheGeneratorRulesAreAllErrors()
    {
        var rows = PageTables.Rows(Page, "The rules");

        Assert.Equal(
            ["JEV001", "JEV002", "JEV003", "JEV004", "JEV005", "JEV006", "JEV101", "JEV102", "JEV103", "JEV104", "JEV105", "JEV106", "JEV107", "JEV108", "JEV109"],
            rows.Select(row => row[0]));
        Assert.All(rows.Where(row => row[0].StartsWith("JEV1", StringComparison.Ordinal)), row => Assert.Equal("Error", row[1]));
    }

    // The warnings and the Info rule are the ones the shared code calls advisory: they never make a set invalid.
    [Fact]
    public void TheWarningsAndInfo_AreTheAdvisoryRules()
    {
        var ids = Source("src", "ZeroAlloc.Jev.Generator", "DiagnosticIds.cs");
        var rows = PageTables.Rows(Page, "The rules");

        Assert.Contains("id is EmptyText or UnknownStateReference or OptionCountOutsideGuidance or MissingCriteria", ids, StringComparison.Ordinal);
        Assert.Equal(
            ["JEV003", "JEV004", "JEV005", "JEV006"],
            rows.Where(row => row[1] is "Warning" or "Info").Select(row => row[0]));
    }

    // JEV005's numbers are JevLimits' numbers, which the generator, the analyzers and Build all share.
    [Fact]
    public void TheJev005Limits_AreJevLimits()
    {
        var limits = Source("src", "ZeroAlloc.Jev.Generator", "JevLimits.cs");
        var row = Array.Find(PageTables.Rows(Page, "The rules"), r => string.Equals(r[0], "JEV005", StringComparison.Ordinal))![3];

        Assert.Contains($"fewer than {Limit(limits, "MinimumScoreLevels")} or more than {Limit(limits, "MaximumScoreLevels")} levels", row, StringComparison.Ordinal);
        Assert.Contains($"more than {Limit(limits, "MaximumChoiceOptions")} options", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCodeFixTable_IsTheProvidersFixableRules_AndItsTitleFormat()
    {
        var provider = Source("src", "ZeroAlloc.Jev.CodeFixes", "AddDescriptionCodeFixProvider.cs");
        var rows = PageTables.Rows(Page, "Code fixes");

        Assert.Contains("ImmutableArray.Create(DiagnosticIds.MissingCriteria, DiagnosticIds.MissingLevel)", provider, StringComparison.Ordinal);
        Assert.Contains("diagnostic.Id == DiagnosticIds.MissingCriteria ? \"Criteria\" : \"Level\"", provider, StringComparison.Ordinal);
        Assert.Contains("$\"Add [{attributeName}(\\\"{WordSplitter.ToSentence(member.Identifier.ValueText)}\\\")]\"", provider, StringComparison.Ordinal);
        Assert.Contains("WellKnownFixAllProviders.BatchFixer", provider, StringComparison.Ordinal);
        Assert.Equal(["JEV006", "JEV104"], rows.Select(row => row[0]));
        Assert.Equal(
            ["Add [Criteria(\"Needs attention\")]", "Add [Level(\"Needs attention\")]"],
            rows.Select(row => PageTables.Code(row[1])));
    }

    // The example sentences on the page are what WordSplitter, which the fix calls, produces; its own tests cover them.
    [Fact]
    public void TheSentencesOnThePage_AreWordSplittersTestedExamples()
    {
        var tests = Source("tests", "ZeroAlloc.Jev.Analyzers.Tests", "WordSplitterTests.cs");

        Assert.Contains("[InlineData(\"NeedsAttention\", \"Needs attention\")]", tests, StringComparison.Ordinal);
        Assert.Contains("[InlineData(\"HTTPError\", \"HTTP error\")]", tests, StringComparison.Ordinal);
        Assert.Contains("[InlineData(\"NEEDS_ATTENTION\", \"Needs attention\")]", tests, StringComparison.Ordinal);
    }

    // The suppression example only compiles, with warnings as errors, because its pragma works. This pins the one thing
    // that makes JEV005 fire: more levels than the guidance allows.
    [Fact]
    public void TheSuppressedEnum_HasMoreLevelsThanJev005Allows()
    {
        var limit = Limit(Source("src", "ZeroAlloc.Jev.Generator", "JevLimits.cs"), "MaximumScoreLevels");

        Assert.True(Enum.GetValues<Recommendation>().Length > limit);
        Assert.Contains("has 11 levels on purpose", PageTables.Text(Page), StringComparison.Ordinal);
        Assert.Equal(11, Enum.GetValues<Recommendation>().Length);
    }

    [Fact]
    public async Task TheSuppressedSet_IsStillAValidSet_ThatEvaluates()
    {
        var reply = await CannedJev.EvaluateAsync<SurveyReply>(
            """
            {
              "model": "jev-1.13.0",
              "answers": { "recommend": { "type": "score", "score": 9.1, "legend": { "0": "0: Not at all likely" }, "probabilities": { "0": 0.0, "1": 0.0, "2": 0.0, "3": 0.0, "4": 0.0, "5": 0.0, "6": 0.0, "7": 0.0, "8": 0.1, "9": 0.8, "10": 0.1 }, "confidence": 0.8 } },
              "usage": { "input_tokens": 30, "output_tokens": 5 }
            }
            """,
            "Great service, I would tell everyone.");

        Assert.Equal(Recommendation.Nine, reply.Recommend.Value);
    }

    private static int Limit(string limits, string name)
        => int.Parse(Regex.Match(limits, $@"{name} = (\d+);", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"public const string (?<name>\w+) = ""(?<id>JEV\d+)"";", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdConstant();

    [GeneratedRegex(
        @"new\(\s*DiagnosticIds\.(?<name>\w+),\s*""(?<title>[^""]*)"",\s*""[^""]*"",\s*Category,\s*DiagnosticSeverity\.(?<severity>\w+),",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Descriptor();

    [GeneratedRegex(@"^(?<id>JEV\d+) \| ZeroAlloc\.Jev \| (?<severity>\w+) \|", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShippedRule();
}
