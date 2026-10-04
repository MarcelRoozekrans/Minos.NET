using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.Docs.Tests;

// The page publishes the client comparison from a checked-in run. These tests keep the page and the run's JSON equal: the
// table must be exactly what merge.py prints for the JSON, and the published run's column under "Across runs" must
// carry the same figures.
public sealed class ComparisonTests
{
    private const string Page = "performance.md";
    private const string Run = "benchmarks/compare/results/ci.json";
    private const string Merge = "benchmarks/compare/merge.py";
    private const string Project = "ZeroAlloc.Jev";

    [Fact]
    public void ThePublishedTable_IsMergesOutputForTheCheckedInRun()
    {
        var expected = RunMerge(Run);
        var published = Block(PageTables.Text(Page), Run);

        Assert.Equal(expected, published);
    }

    [Fact]
    public void ThePublishedRunsColumnUnderAcrossRuns_IsTheCheckedInRun()
    {
        var results = Results(Run);
        var rows = PageTables.Rows(Page, "Across runs");

        Assert.Equal(
            results.Select(r => r.GetProperty("client").GetString()).Order(StringComparer.Ordinal),
            rows.Select(row => row[0].Trim('*')).Order(StringComparer.Ordinal));
        Assert.All(
            rows,
            row =>
            {
                var client = row[0].Trim('*');
                var result = results.Single(r => string.Equals(r.GetProperty("client").GetString(), client, StringComparison.Ordinal));

                Assert.Equal(Whole(result.GetProperty("throughputPerSecond").GetDouble()), Last(row[1]));
                Assert.Equal(
                    result.GetProperty("latencyMs").GetProperty("mean").GetDouble().ToString("F3", CultureInfo.InvariantCulture),
                    Last(row[2]));
                var bytes = result.GetProperty("allocatedBytesPerCall");
                Assert.Equal(bytes.ValueKind == JsonValueKind.Null ? "—" : Whole(bytes.GetDouble()), Last(row[3]));
            });
    }

    [Fact]
    public void Block_IsTheTextBetweenTheMarkers()
    {
        var text = "a\n<!-- comparison: x.json -->\n### T\n\n| r |\n<!-- endComparison -->\nb\n";

        Assert.Equal("### T\n\n| r |\n", Block(text, "x.json"));
        Assert.Equal("### T\n\n| r |\n", Block(text.Replace("\n", "\r\n", StringComparison.Ordinal), "x.json"));
    }

    [Theory]
    [InlineData("<!-- endComparison -->\n")]
    [InlineData("<!-- comparison: other.json -->\nx\n<!-- endComparison -->\n")]
    [InlineData("<!-- comparison: x.json -->\nx\n")]
    [InlineData("<!-- comparison: x.json -->\nx\n<!-- endComparison -->\n<!-- comparison: x.json -->\ny\n<!-- endComparison -->\n")]
    public void Block_NeedsExactlyOnePairOfMarkersForTheRun(string text) =>
        Assert.ThrowsAny<Exception>(() => Block(text, "x.json"));

    private static string Whole(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    // "96,502 / 49,550 / 36,869": the published run is the last of the runs in a cell.
    private static string Last(string cell) => cell.Split(" / ")[^1];

    private static JsonElement[] Results(string run)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, run)));
        return [.. document.RootElement.GetProperty("files").EnumerateArray()
            .SelectMany(file => file.GetProperty("results").EnumerateArray())
            .Select(result => result.Clone())];
    }

    // The lines between "<!-- comparison: <run> -->" and "<!-- endComparison -->", with LF line ends.
    private static string Block(string text, string run)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = $"<!-- comparison: {run} -->";
        var starts = Enumerable.Range(0, lines.Length).Where(i => lines[i].StartsWith("<!-- comparison:", StringComparison.Ordinal)).ToArray();
        var ends = Enumerable.Range(0, lines.Length).Where(i => string.Equals(lines[i], "<!-- endComparison -->", StringComparison.Ordinal)).ToArray();

        Assert.True(starts.Length == 1 && ends.Length == 1, "The page has one comparison block.");
        Assert.Equal(start, lines[starts[0]]);
        Assert.True(ends[0] > starts[0], "The block's end marker follows its start marker.");
        return string.Join('\n', lines[(starts[0] + 1)..ends[0]]) + "\n";
    }

    private static string RunMerge(string run)
    {
        var python = Python();
        var start = new ProcessStartInfo(python)
        {
            WorkingDirectory = PublishedPages.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { Merge, run, "--project", Project })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(1)), "merge.py finished within a minute.");
        Assert.True(process.ExitCode == 0, $"merge.py exited with {process.ExitCode}: {error.Result}");
        return output.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    // CI's ubuntu-latest has python3; Windows has python or the py launcher. A missing Python fails the test, so the
    // page is never left unchecked.
    private static string Python()
    {
        string[] candidates = OperatingSystem.IsWindows() ? ["python", "py", "python3"] : ["python3", "python"];
        foreach (var candidate in candidates)
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                })!;
                probe.StandardOutput.ReadToEnd();
                probe.StandardError.ReadToEnd();
                if (probe.WaitForExit(TimeSpan.FromSeconds(30)) && probe.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch (Win32Exception)
            {
                // Not installed under this name; try the next.
            }
        }

        Assert.Fail($"No Python interpreter found as {string.Join(", ", candidates)}; the comparison test needs one to run {Merge}.");
        return string.Empty;
    }
}
