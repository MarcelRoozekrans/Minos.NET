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

        Assert.Equal(13, Descriptors().Length);
        Assert.Equal(
            Descriptors().Select(rule => $"{rule.Id} {rule.Severity} {rule.Title}"),
            rows.Select(row => $"{row[0]} {row[1]} {row[2]}"));
    }

    // The release-tracking files are a second record of the same ids and severities, checked by the build's own
    // analyzer: the shipped rules, less the ones the unshipped release removes.
    [Fact]
    public void TheRuleTableOnThePage_IsTheShippedAnalyzerReleaseToo()
    {
        var rows = PageTables.Rows(Page, "The rules");
        var shipped = Rules(Source("src", "ZeroAlloc.Jev.Analyzers", "AnalyzerReleases.Shipped.md"));
        var removed = Rules(RemovedRulesSection(Source("src", "ZeroAlloc.Jev.Analyzers", "AnalyzerReleases.Unshipped.md")));

        Assert.Equal(shipped.Except(removed, StringComparer.Ordinal), rows.Select(row => $"{row[0]} {row[1]}"));
    }

    // A shipped release is never edited: the rules JEV108 and JEV109 lost with Json = true stay in it, and the unshipped
    // release records their removal.
    [Fact]
    public void Jev108AndJev109_StayShipped_AndAreRemovedInTheUnshippedRelease()
    {
        var shipped = Rules(Source("src", "ZeroAlloc.Jev.Analyzers", "AnalyzerReleases.Shipped.md"));
        var removed = Rules(RemovedRulesSection(Source("src", "ZeroAlloc.Jev.Analyzers", "AnalyzerReleases.Unshipped.md")));

        Assert.Contains("JEV108 Error", shipped);
        Assert.Contains("JEV109 Error", shipped);
        Assert.Equal(["JEV108 Error", "JEV109 Error"], removed);
    }

    [Fact]
    public void TheGroups_AreTheSixApiRules_AndSevenGeneratorRules_AndTheGeneratorRulesAreAllErrors()
    {
        var rows = PageTables.Rows(Page, "The rules");

        Assert.Equal(
            ["JEV001", "JEV002", "JEV003", "JEV004", "JEV005", "JEV006", "JEV101", "JEV102", "JEV103", "JEV104", "JEV105", "JEV106", "JEV107"],
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

    // The run-time builder's own rule: its id is DiagnosticIds.InvalidJson and its depth is JevLimits.MaximumJsonDepth,
    // which Build checks. It has no analyzer descriptor, so it is not in the analyzer table.
    [Fact]
    public void TheBuilderSection_IsInvalidJson_AtJevLimitsDepth()
    {
        var limits = Source("src", "ZeroAlloc.Jev.Generator", "JevLimits.cs");
        var invalidJson = Regex.Match(
            Source("src", "ZeroAlloc.Jev.Generator", "DiagnosticIds.cs"),
            @"public const string InvalidJson = ""(?<id>JEV\d+)"";",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)).Groups["id"].Value;
        var rows = PageTables.Rows(Page, "Reported by the run-time builder");

        Assert.Equal("JEV108", invalidJson);
        Assert.Equal([invalidJson], rows.Select(row => row[0]));
        Assert.Contains($"more than {Limit(limits, "MaximumJsonDepth")} levels", rows[0][2], StringComparison.Ordinal);
        Assert.DoesNotContain(Descriptors(), rule => string.Equals(rule.Id, invalidJson, StringComparison.Ordinal));
        Assert.Contains(
            "### Reported by the run-time builder\n\nOne rule has no analyzer, because only a [set built at run time](question-sets-at-run-time.md#checking-the-set)",
            PageTables.Text(Page).ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
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

    private static List<string> Rules(string release)
    {
        var rules = new List<string>();
        foreach (Match match in ShippedRule().Matches(release))
        {
            rules.Add($"{match.Groups["id"].Value} {match.Groups["severity"].Value}");
        }

        return rules;
    }

    private static string RemovedRulesSection(string release)
    {
        var start = release.IndexOf("### Removed Rules", StringComparison.Ordinal);
        Assert.True(start >= 0, "AnalyzerReleases.Unshipped.md has a Removed Rules section.");
        var end = release.IndexOf("###", start + 3, StringComparison.Ordinal);
        return end < 0 ? release[start..] : release[start..end];
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
