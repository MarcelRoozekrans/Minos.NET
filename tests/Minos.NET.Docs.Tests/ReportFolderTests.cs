using System.Text.Json;
using Minos.Evaluation;
using Minos.Tool;

namespace Minos.Docs.Tests;

// Keeps every committed calibration report folder, docs/reports/<provider>/<set>/, honest: it has its files, it is a
// known provider and set, its README says what its report says, and its report.json is exactly what the library builds
// again from the folder's results, the dataset and the prices, so no report was edited by hand.
public sealed class ReportFolderTests
{
    private static readonly string ReportsRoot = Path.Combine(PublishedPages.Root, "docs", "reports");

    private static readonly string[] Providers = ["typesafe", "clef-flash", "local"];

    private static readonly Dictionary<string, string> DatasetFiles = new()
    {
        ["banking77"] = "banking77-minos-v1.jsonl",
        ["urgency"] = "banking77-urgency-v1.jsonl",
    };

    // The workflow run each folder's figures came from. A new folder needs an entry here.
    private static readonly Dictionary<string, long> RunIds = new()
    {
        ["clef-flash/urgency"] = 39053318208,
        ["clef-flash/banking77"] = 39053417194,
        ["typesafe/urgency"] = 39053418745,
        ["typesafe/banking77"] = 39053418745,
        ["local/urgency"] = 39053226413,
        ["local/banking77"] = 39053226413,
    };

    private static readonly string[] ReadmeFields = ["Date", "Provider", "Model", "Dataset", "Run", "Runner", "Served", "Tool", "Prices"];

    public static TheoryData<string> Folders()
    {
        var data = new TheoryData<string>();
        foreach (var folder in FolderPaths())
        {
            data.Add(Path.GetRelativePath(ReportsRoot, folder).Replace('\\', '/'));
        }

        return data;
    }

    [Fact]
    public void TheReportsFolder_HoldsAtLeastOneReport()
    {
        Assert.NotEmpty(FolderPaths());
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryReportFolder_HasItsFiles(string folder)
    {
        var path = Path.Combine(ReportsRoot, folder);

        foreach (var name in new[] { "report.json", "report.md", "README.md" })
        {
            Assert.True(File.Exists(Path.Combine(path, name)), folder + " has no " + name);
        }

        var results = File.Exists(Path.Combine(path, "results.jsonl"));
        var partial = File.Exists(Path.Combine(path, "results.partial.jsonl"));
        Assert.True(results ^ partial, folder + " needs exactly one of results.jsonl and results.partial.jsonl");

        var expected = new[] { "README.md", "report.json", "report.md", results ? "results.jsonl" : "results.partial.jsonl" };
        var actual = Directory.GetFileSystemEntries(path).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryReportFolder_IsAKnownProviderAndSet(string folder)
    {
        var parts = folder.Split('/');

        Assert.Equal(2, parts.Length);
        Assert.Contains(parts[0], Providers);
        Assert.Contains(parts[1], DatasetFiles.Keys);
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryReportReadme_MatchesItsReport(string folder)
    {
        var path = Path.Combine(ReportsRoot, folder);
        var fields = ReadmeValues(File.ReadAllLines(Path.Combine(path, "README.md")));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "report.json")));
        var header = report.RootElement.GetProperty("header");

        foreach (var field in ReadmeFields)
        {
            Assert.True(fields.TryGetValue(field, out var value) && value.Length > 0, folder + " README has no " + field + ": line");
        }

        Assert.Equal(header.GetProperty("providerName").GetString(), fields["Provider"]);
        Assert.Equal(header.GetProperty("defaultModel").GetString(), fields["Model"]);
        Assert.Equal(header.GetProperty("datasetName").GetString(), fields["Dataset"]);
        var responseModels = report.RootElement.GetProperty("responseModels").EnumerateArray()
            .Select(entry => entry.GetProperty("model").GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(string.Join(", ", responseModels), fields["Served"]);
        Assert.True(RunIds.TryGetValue(folder, out var run), folder + " has no expected run id; add it to RunIds in ReportFolderTests");
        Assert.Equal("https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/" + run, fields["Run"]);
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryReport_RebuildsFromItsResults(string folder)
    {
        var path = Path.Combine(ReportsRoot, folder);
        var resultsFile = Path.Combine(path, File.Exists(Path.Combine(path, "results.jsonl")) ? "results.jsonl" : "results.partial.jsonl");
        var datasetFile = Path.Combine(PublishedPages.Root, "datasets", "banking77", DatasetFiles[folder.Split('/')[1]]);

        var results = EvaluationResults.Load(resultsFile);
        Assert.True(results.IsSuccess, folder + " results: " + (results.IsFailure ? Describe(results.Error) : string.Empty));
        var dataset = DecisionDataset.Load(datasetFile);
        Assert.True(dataset.IsSuccess, folder + " dataset: " + (dataset.IsFailure ? Describe(dataset.Error) : string.Empty));
        var prices = PricesFile.Load(Path.Combine(ReportsRoot, "prices.json"));
        Assert.True(prices.IsSuccess, prices.IsFailure ? prices.Error : string.Empty);

        var rebuilt = CalibrationReport.Create(results.Value, dataset.Value, prices.Value);

        Assert.True(rebuilt.IsSuccess, folder + " report: " + (rebuilt.IsFailure ? Describe(rebuilt.Error) : string.Empty));
        Assert.Equal(File.ReadAllText(Path.Combine(path, "report.json")), rebuilt.Value.ToJson());
        Assert.Equal(File.ReadAllText(Path.Combine(path, "report.md")), rebuilt.Value.ToMarkdown());
    }

    private static string Describe(DatasetProblems problems)
        => string.Join("; ", problems.Select(problem => problem.ToString()));

    private static IEnumerable<string> FolderPaths()
        => Directory.Exists(ReportsRoot)
            ? Directory.GetDirectories(ReportsRoot).SelectMany(Directory.GetDirectories).OrderBy(folder => folder, StringComparer.Ordinal)
            : [];

    // README lines of the form "Name: value", with an optional leading bold or list marker removed.
    private static Dictionary<string, string> ReadmeValues(string[] lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            var line = raw.TrimStart('-', '*', ' ');
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            values.TryAdd(line[..colon], line[(colon + 1)..].Trim());
        }

        return values;
    }
}
