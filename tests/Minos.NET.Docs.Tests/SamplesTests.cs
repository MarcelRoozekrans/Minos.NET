using System.Text.Json;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

/// <summary>Ties the samples page to the sample folders, their recordings and the CI workflow.</summary>
public sealed partial class SamplesTests
{
    private static string Samples { get; } = Path.Combine(PublishedPages.Root, "samples");

    /// <summary>The folders under samples/ that hold a recordings.json: the cookbook samples.</summary>
    private static string[] SampleFolders { get; } = Directory.EnumerateDirectories(Samples)
        .Where(dir => File.Exists(Path.Combine(dir, "recordings.json")))
        .Select(dir => Path.GetFileName(dir))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void TheTable_ListsEverySampleFolder_AndNothingElse()
    {
        var rows = PageTables.Rows("samples.md", "The three samples");

        var listed = rows.Select(row => Folder(row[0])).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(SampleFolders, listed);
        Assert.Equal(3, SampleFolders.Length);
    }

    [Fact]
    public void EachRow_HasTheRealCommandAndTheRecordedRequestCount()
    {
        foreach (var row in PageTables.Rows("samples.md", "The three samples"))
        {
            var folder = Folder(row[0]);

            Assert.Equal($"dotnet run --project samples/{folder}", PageTables.Code(row[2]));
            using var recordings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Samples, folder, "recordings.json")));
            var entries = recordings.RootElement.GetProperty("entries").GetArrayLength();
            Assert.Equal(entries.ToString(System.Globalization.CultureInfo.InvariantCulture), row[3]);
            Assert.True(File.Exists(Path.Combine(Samples, folder, "README.md")), folder + " has a README.");
        }
    }

    [Fact]
    public void EverySampleProgram_AcceptsTheThreeModes_AndExitsWithTwoOtherwise()
    {
        var page = PageTables.Text("samples.md");

        foreach (var folder in SampleFolders)
        {
            var program = File.ReadAllText(Path.Combine(Samples, folder, "Program.cs"));
            Assert.Contains("[-- --replay | --live | --record]", program, StringComparison.Ordinal);
            Assert.Contains("return 2;", program, StringComparison.Ordinal);
        }

        Assert.All(["`--replay`", "`--live`", "`--record`", "exits with code 2"], mode => Assert.Contains(mode, page, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecordings_AreFromTheModelAndDateThePageNames()
    {
        var page = PageTables.Text("samples.md");

        foreach (var folder in SampleFolders)
        {
            using var recordings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Samples, folder, "recordings.json")));
            Assert.Equal("OpenRouter", recordings.RootElement.GetProperty("provider").GetString());
            Assert.Contains("`" + recordings.RootElement.GetProperty("model").GetString() + "`", page, StringComparison.Ordinal);
            Assert.Contains(recordings.RootElement.GetProperty("recorded").GetString()!, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheLiveModes_NeedTheOpenRouterKeyVariable()
    {
        Assert.Contains("`" + DecisionDefaults.OpenRouterApiKeyEnvironmentVariable + "`", PageTables.Text("samples.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCiStep_RunsEveryRecordedSampleInReplayWithAnEmptyKey()
    {
        var workflow = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "ci.yml"));

        Assert.Contains("for recordings in samples/*/recordings.json; do", workflow, StringComparison.Ordinal);
        Assert.Contains("OPENROUTER_API_KEY: \"\"", workflow, StringComparison.Ordinal);
        Assert.Contains("test \"$status\" -eq 0", workflow, StringComparison.Ordinal);
        Assert.Contains("test -s \"$out\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--live", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--record", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingsLiveInTheSourceFolder_FoundBySolutionFile()
    {
        var host = File.ReadAllText(Path.Combine(Samples, "Minos.NET.Samples.Shared", "SampleHost.cs"));

        Assert.Contains("Minos.NET.slnx", host, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(dir.FullName, \"samples\", sampleName)", host, StringComparison.Ordinal);
        Assert.Contains("mode == SampleMode.Live ? null", host, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAotSmokeApp_IsNotASample()
    {
        Assert.DoesNotContain("Minos.NET.AotSmoke", SampleFolders);
        Assert.True(Directory.Exists(Path.Combine(Samples, "Minos.NET.AotSmoke")));
    }

    private static string Folder(string cell)
    {
        var match = SampleLink().Match(cell);
        Assert.True(match.Success, $"'{cell}' links a sample folder on GitHub.");
        var folder = match.Groups["folder"].Value;
        Assert.True(Directory.Exists(Path.Combine(Samples, folder)), folder + " exists.");
        return folder;
    }

    [GeneratedRegex(
        """^\[[^\]]+\]\(https://github\.com/MarcelRoozekrans/Minos\.NET/tree/main/samples/(?<folder>[A-Za-z0-9.]+)\)$""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SampleLink();
}
