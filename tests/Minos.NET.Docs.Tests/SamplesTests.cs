using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Minos.SystemOne;

namespace Minos.Docs.Tests;

/// <summary>Ties the samples page to the sample folders, their recordings and the CI workflows.</summary>
public sealed partial class SamplesTests
{
    private static string Samples { get; } = Path.Combine(PublishedPages.Root, "samples");

    /// <summary>The cookbook samples: every samples/Minos.NET.Samples.* folder except the shared library.</summary>
    private static string[] SampleFolders { get; } = Directory.EnumerateDirectories(Samples, "Minos.NET.Samples.*")
        .Select(dir => Path.GetFileName(dir))
        .Where(name => !string.Equals(name, "Minos.NET.Samples.Shared", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>The samples with one recordings.json. The provider-switch sample keeps one file per provider in recordings/.</summary>
    private static string[] SingleRecordingFolders { get; } = SampleFolders
        .Where(folder => File.Exists(Path.Combine(Samples, folder, "recordings.json")))
        .ToArray();

    [Fact]
    public void TheProviderRecordings_AreFromTheModelsAndDatesThePageNames()
    {
        var page = PageTables.Text("samples.md");
        var files = SampleFolders.Except(SingleRecordingFolders, StringComparer.Ordinal)
            .SelectMany(folder => Directory.GetFiles(Path.Combine(Samples, folder, "recordings"), "*.json"))
            .ToArray();

        Assert.Equal(3, files.Length);
        foreach (var file in files)
        {
            using var recordings = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Contains("`" + recordings.RootElement.GetProperty("model").GetString() + "`", page, StringComparison.Ordinal);
            Assert.Contains(recordings.RootElement.GetProperty("recorded").GetString()!, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheTable_ListsEverySampleFolder_AndNothingElse()
    {
        var rows = PageTables.Rows("samples.md", "The four samples");

        var listed = rows.Select(row => Folder(row[0])).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(SampleFolders, listed);
        Assert.Equal(4, SampleFolders.Length);
        Assert.Equal(3, SingleRecordingFolders.Length);
    }

    [Fact]
    public void EachRow_HasTheRealCommandAndTheRecordedRequestCount()
    {
        foreach (var row in PageTables.Rows("samples.md", "The four samples"))
        {
            var folder = Folder(row[0]);

            Assert.Equal($"dotnet run --project samples/{folder}", PageTables.Code(row[2]));
            Assert.True(File.Exists(Path.Combine(Samples, folder, "README.md")), folder + " has a README.");
            var single = Path.Combine(Samples, folder, "recordings.json");
            if (File.Exists(single))
            {
                Assert.Equal(Entries(single).ToString(CultureInfo.InvariantCulture), row[3]);
                continue;
            }

            // A provider sample sends the same requests to each provider, so each provider's file holds one answer per
            // request. Its count against the sample's own tickets is checked in ProviderSwitchSampleTests.
            var perProvider = PerProvider().Match(row[3]);
            Assert.True(perProvider.Success, $"'{row[3]}' reads '<n> per provider'.");
            var count = int.Parse(perProvider.Groups["count"].Value, CultureInfo.InvariantCulture);
            var recordings = Path.Combine(Samples, folder, "recordings");
            foreach (var file in Directory.Exists(recordings) ? Directory.GetFiles(recordings, "*.json") : Array.Empty<string>())
            {
                Assert.Equal(count, Entries(file));
            }
        }
    }

    [Fact]
    public void EverySampleProgram_AcceptsTheThreeModes_AndExitsWithTwoOtherwise()
    {
        var page = PageTables.Text("samples.md");

        foreach (var folder in SampleFolders)
        {
            var code = new StringBuilder();
            foreach (var file in Directory.GetFiles(Path.Combine(Samples, folder), "*.cs"))
            {
                code.Append(File.ReadAllText(file));
            }

            Assert.All(["--replay", "--live", "--record", "return 2;"], text => Assert.Contains(text, code.ToString(), StringComparison.Ordinal));
        }

        Assert.All(
            ["`--replay`", "`--live`", "`--record`", "`--provider <name>`", "exits with code 2"],
            mode => Assert.Contains(mode, page, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecordings_AreFromTheModelAndDateThePageNames()
    {
        var page = PageTables.Text("samples.md");

        foreach (var folder in SingleRecordingFolders)
        {
            using var recordings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Samples, folder, "recordings.json")));
            Assert.Equal("OpenRouter", recordings.RootElement.GetProperty("provider").GetString());
            Assert.Contains("`" + recordings.RootElement.GetProperty("model").GetString() + "`", page, StringComparison.Ordinal);
            Assert.Contains(recordings.RootElement.GetProperty("recorded").GetString()!, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheLiveModes_NeedTheKeyVariables()
    {
        var page = PageTables.Text("samples.md");

        Assert.Contains("`" + SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable + "`", page, StringComparison.Ordinal);
        Assert.Contains("`" + SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable + "`", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCiStep_RunsEveryRecordedSampleInReplayWithEmptyKeys()
    {
        var workflow = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "ci.yml"));

        Assert.Contains("shopt -s nullglob", workflow, StringComparison.Ordinal);
        Assert.Contains("for recordings in samples/*/recordings.json; do", workflow, StringComparison.Ordinal);
        Assert.Contains("for recordings in samples/*/recordings/*.json; do", workflow, StringComparison.Ordinal);
        Assert.Contains("-- --provider \"$provider\"", workflow, StringComparison.Ordinal);
        Assert.Contains("OPENROUTER_API_KEY: \"\"", workflow, StringComparison.Ordinal);
        Assert.Contains("TYPESAFE_API_KEY: \"\"", workflow, StringComparison.Ordinal);
        Assert.Contains("test \"$status\" -eq 0", workflow, StringComparison.Ordinal);
        Assert.Contains("test -s \"$out\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--live", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--record", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordingWorkflows_RecordEachProviderOfTheSwitchSample_AndUploadTheFiles()
    {
        var live = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "live-smoke.yml"));
        var local = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "local-systemone.yml"));
        const string Run = "dotnet run --project samples/Minos.NET.Samples.ProviderSwitch -c Release -- --provider ";

        Assert.Contains(Run + "typesafe --record", live, StringComparison.Ordinal);
        Assert.Contains(Run + "openrouter --record", live, StringComparison.Ordinal);
        Assert.Contains("path: samples/Minos.NET.Samples.ProviderSwitch/recordings/", live, StringComparison.Ordinal);
        Assert.Contains(Run + "local --record", local, StringComparison.Ordinal);
        Assert.Contains("name: provider-switch-recordings", live, StringComparison.Ordinal);
        Assert.Contains("name: provider-switch-local", local, StringComparison.Ordinal);
        var typeSafeStep = StepBlock(live, "Record the provider-switch sample against TypeSafe");
        var openRouterStep = StepBlock(live, "Record the provider-switch sample against OpenRouter");
        Assert.Contains("TYPESAFE_API_KEY: ${{ secrets.TYPESAFE_API_KEY }}", typeSafeStep, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENROUTER_API_KEY", typeSafeStep, StringComparison.Ordinal);
        Assert.Contains("OPENROUTER_API_KEY: ${{ secrets.OPENROUTER_API_KEY }}", openRouterStep, StringComparison.Ordinal);
        Assert.DoesNotContain("TYPESAFE_API_KEY", openRouterStep, StringComparison.Ordinal);
        Assert.Equal(3, CountOf("if: ${{ !cancelled() }}", live));
        Assert.Equal(2, CountOf("if: ${{ !cancelled() }}", local));
        Assert.Contains("path: samples/Minos.NET.Samples.ProviderSwitch/recordings/local.json", local, StringComparison.Ordinal);
    }

    private static string StepBlock(string workflow, string stepName)
    {
        var start = workflow.IndexOf("- name: " + stepName, StringComparison.Ordinal);
        Assert.True(start >= 0, stepName);
        var next = workflow.IndexOf("\n      - name:", start + 1, StringComparison.Ordinal);
        return next < 0 ? workflow[start..] : workflow[start..next];
    }

    private static int CountOf(string needle, string text) =>
        text.Split(needle, StringSplitOptions.None).Length - 1;

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

    private static int Entries(string path)
    {
        using var recordings = JsonDocument.Parse(File.ReadAllText(path));
        return recordings.RootElement.GetProperty("entries").GetArrayLength();
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

    [GeneratedRegex("^(?<count>[0-9]+) per provider$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PerProvider();
}
