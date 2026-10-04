using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZeroAlloc.Jev.Docs.Tests;

// The page publishes the client comparison from checked-in runs. These tests keep every figure the Comparison section
// states tied to the runs' JSON: the tables must be exactly what merge.py prints for the JSON, and the section's own
// prose may state no measured figure, only the method's parameters.
public sealed partial class ComparisonTests
{
    private const string Page = "performance.md";
    private const string Run = "benchmarks/compare/results/ci-run-1.json";
    private const string Merge = "benchmarks/compare/merge.py";
    private const string Project = "ZeroAlloc.Jev";
    private const string TableStart = "<!-- comparison:";
    private const string TableEnd = "<!-- endComparison -->";
    private const string AcrossStart = "<!-- acrossRuns:";
    private const string AcrossEnd = "<!-- endAcrossRuns -->";

    // The numbers the section's prose may state: the method's parameters, which the harnesses fix and the result files
    // do not all record. 16 workers is also checked against every result's concurrency, and the latency rounds against
    // a run's recorded order when it has one. Any other number in the prose is a measured figure stated outside the
    // generated tables, and fails the test.
    private static readonly string[] MethodParameters =
    [
        "16", // workers in the warm-up and the throughput run
        "2", // seconds of warm-up; minutes a pooled connection lives
        "10", // seconds of the throughput run
        "200", // warm-up calls before the latency loop
        "2000", // timed latency calls per client
        "20", // interleaved latency rounds
        "100", // timed calls per client per round
        "32", // the mock ceiling's other worker counts
        "64",
    ];

    [Fact]
    public void ThePublishedTable_IsMergesOutputForTheCheckedInRun()
    {
        var expected = RunMerge(Merge, Run, "--project", Project);
        var published = Block(PageTables.Text(Page), TableStart, TableEnd, Run);

        Assert.Equal(expected, published);
    }

    [Fact]
    public void TheAcrossRunsTables_AreMergesOutputForTheRunsTheMarkerNames()
    {
        var runs = AcrossRuns(PageTables.Text(Page));
        var expected = RunMerge([Merge, "--across", .. runs, "--project", Project]);
        var published = Block(PageTables.Text(Page), AcrossStart, AcrossEnd, string.Join(' ', runs));

        Assert.Contains(Run, runs);
        Assert.Equal(expected, published);
    }

    // The prose under the main table claims, for every run the across-runs marker names, that ZeroAlloc.Jev has the
    // lowest mean latency, the highest throughput and the fewest bytes per call, and that the other clients' throughput
    // order is the one it lists. A republished run that breaks a claim fails here, so the prose is re-read.
    [Fact]
    public void TheProsesClaims_HoldInEveryRun()
    {
        string[] stated = ["raw-httpclient", "jev-net", "typesafe-ai-sdk", "jevsharp", "typesafe-ai-sdk-js", "typesafe-sdk-python"];
        var runs = AcrossRuns(PageTables.Text(Page));

        Assert.Equal(3, runs.Length);
        Assert.All(runs, run =>
        {
            var results = Files(run).SelectMany(file => file.GetProperty("results").EnumerateArray()).ToArray();
            var jev = results.Single(r => string.Equals(r.GetProperty("client").GetString(), "zeroalloc-jev", StringComparison.Ordinal));
            var others = results.Where(r => !string.Equals(r.GetProperty("client").GetString(), "zeroalloc-jev", StringComparison.Ordinal)).ToArray();

            Assert.All(others, other =>
            {
                Assert.True(Mean(other) > Mean(jev), $"{run}: ZeroAlloc.Jev's mean is below {Client(other)}'s.");
                Assert.True(Throughput(other) < Throughput(jev), $"{run}: ZeroAlloc.Jev's throughput is above {Client(other)}'s.");
                var bytes = other.GetProperty("allocatedBytesPerCall");
                Assert.True(
                    bytes.ValueKind == JsonValueKind.Null || bytes.GetInt64() > jev.GetProperty("allocatedBytesPerCall").GetInt64(),
                    $"{run}: ZeroAlloc.Jev allocates less than {Client(other)}.");
            });
            Assert.Equal(stated, others.OrderByDescending(Throughput).Select(Client));
        });
    }

    // The prose under the main table says that in every run the raw client and ZeroAlloc.Jev come close to the mock
    // ceiling, and that the other clients stay well below it. Close means at least 90% of the ceiling, and well below
    // means under 75%, so the two groups are apart by a clear margin in every run.
    [Fact]
    public void TheCeilingClaims_HoldInEveryRun()
    {
        string[] leaders = ["raw-httpclient", "zeroalloc-jev"];
        var page = PageTables.Text(Page);

        Assert.Contains("In every run the raw client and ZeroAlloc.Jev come close to the mock ceiling", page, StringComparison.Ordinal);
        Assert.Contains("The other clients stay well below it in every run", page, StringComparison.Ordinal);
        Assert.All(AcrossRuns(page), run =>
        {
            var files = Files(run);
            var ceiling = files
                .Select(file => file.GetProperty("machine").GetProperty("mockCeilingPerSecond"))
                .First(value => value.ValueKind != JsonValueKind.Null)
                .GetDouble();
            Assert.All(files.SelectMany(file => file.GetProperty("results").EnumerateArray()), result =>
            {
                var share = Throughput(result) / ceiling;
                if (leaders.Contains(Client(result), StringComparer.Ordinal))
                {
                    Assert.True(share >= 0.90, $"{run}: {Client(result)} reaches {share:P0} of the ceiling, not close to it.");
                }
                else
                {
                    Assert.True(share < 0.75, $"{run}: {Client(result)} reaches {share:P0} of the ceiling, not well below it.");
                }
            });
        });
    }

    private static string Client(JsonElement result) => result.GetProperty("client").GetString()!;

    private static double Mean(JsonElement result) => result.GetProperty("latencyMs").GetProperty("mean").GetDouble();

    private static double Throughput(JsonElement result) => result.GetProperty("throughputPerSecond").GetDouble();

    [Fact]
    public void TheComparisonProse_StatesNoMeasuredFigureOfItsOwn()
    {
        var prose = ComparisonProse(PageTables.Text(Page));
        var numbers = Number().Matches(prose).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToArray();

        Assert.All(numbers, number => Assert.True(
            MethodParameters.Contains(number, StringComparer.Ordinal),
            $"The Comparison section's prose states {number}, which is not one of the method's parameters. Put measured figures in a generated table, or tie the number to the JSON in this test."));
        Assert.Contains("16", numbers);
    }

    [Fact]
    public void TheMethodsParametersInTheProse_MatchTheRuns()
    {
        string[] runs = [.. AcrossRuns(PageTables.Text(Page)).Append(Run).Distinct(StringComparer.Ordinal)];
        var files = runs.SelectMany(Files).ToArray();

        Assert.All(files.SelectMany(file => file.GetProperty("results").EnumerateArray()), result => Assert.Equal(16, result.GetProperty("concurrency").GetInt32()));
        Assert.All(files.Where(file => file.TryGetProperty("order", out _)), file =>
        {
            var order = file.GetProperty("order");
            Assert.Equal(20, order.GetProperty("latencyRounds").GetInt32());
            Assert.Equal(100, order.GetProperty("callsPerRound").GetInt32());
        });
    }

    [Fact]
    public void TheProse_LeavesOutTheTablesTheLinkTargetsAndTheIssueNumbers()
    {
        string[] lines =
        [
            "## Comparison",
            "Ran 16 workers; see [run 123](https://x/runs/123), [issue #97][issue-97] and #98.",
            "<!-- comparison: x.json -->",
            "| a | 5,808 |",
            "<!-- endComparison -->",
            "<!-- acrossRuns: x.json -->",
            "| b | 91% |",
            "<!-- endAcrossRuns -->",
            "[ref-1]: https://x/runs/456",
            "## Next",
            "Not 17.",
        ];

        var prose = ComparisonProse(string.Join('\n', lines));

        Assert.Equal(["16", "123"], Number().Matches(prose).Select(m => m.Value));
    }

    [Fact]
    public void Block_IsTheTextBetweenTheMarkers()
    {
        var text = "a\n<!-- comparison: x.json -->\n### T\n\n| r |\n<!-- endComparison -->\nb\n";

        Assert.Equal("### T\n\n| r |\n", Block(text, TableStart, TableEnd, "x.json"));
        Assert.Equal("### T\n\n| r |\n", Block(text.Replace("\n", "\r\n", StringComparison.Ordinal), TableStart, TableEnd, "x.json"));
    }

    [Theory]
    [InlineData("<!-- endComparison -->\n")]
    [InlineData("<!-- comparison: other.json -->\nx\n<!-- endComparison -->\n")]
    [InlineData("<!-- comparison: x.json -->\nx\n")]
    [InlineData("<!-- comparison: x.json -->\nx\n<!-- endComparison -->\n<!-- comparison: x.json -->\ny\n<!-- endComparison -->\n")]
    public void Block_NeedsExactlyOnePairOfMarkersForTheRun(string text) =>
        Assert.ThrowsAny<Exception>(() => Block(text, TableStart, TableEnd, "x.json"));

    // The files "<!-- acrossRuns: a.json b.json -->" names, in order.
    private static string[] AcrossRuns(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(l => l.StartsWith(AcrossStart, StringComparison.Ordinal)).ToArray();
        Assert.True(lines.Length == 1, "The page has one across-runs block.");
        return lines[0][AcrossStart.Length..^"-->".Length].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    // Each harness file a published run holds, cloned so it outlives the document.
    private static JsonElement[] Files(string run)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, run)));
        return [.. document.RootElement.GetProperty("files").EnumerateArray().Select(file => file.Clone())];
    }

    // The Comparison section without its generated blocks, its link targets, its reference definitions and its issue
    // numbers: what the page states in its own words.
    private static string ComparisonProse(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.IndexOf(lines, "## Comparison");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.True(start >= 0 && end > start, "The page has a Comparison section followed by another section.");

        var prose = new StringBuilder();
        var generated = false;
        foreach (var line in lines[start..end])
        {
            if (line.StartsWith(TableStart, StringComparison.Ordinal) || line.StartsWith(AcrossStart, StringComparison.Ordinal))
            {
                generated = true;
            }
            else if (string.Equals(line, TableEnd, StringComparison.Ordinal) || string.Equals(line, AcrossEnd, StringComparison.Ordinal))
            {
                generated = false;
            }
            else if (!generated && !ReferenceDefinition().IsMatch(line))
            {
                prose.Append(LinkTarget().Replace(IssueNumber().Replace(line, string.Empty), "]")).Append('\n');
            }
        }

        return prose.ToString();
    }

    // The lines between "<start> <key> -->" and the end marker, with LF line ends.
    private static string Block(string text, string startMarker, string endMarker, string key)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = $"{startMarker} {key} -->";
        var starts = Enumerable.Range(0, lines.Length).Where(i => lines[i].StartsWith(startMarker, StringComparison.Ordinal)).ToArray();
        var ends = Enumerable.Range(0, lines.Length).Where(i => string.Equals(lines[i], endMarker, StringComparison.Ordinal)).ToArray();

        Assert.True(starts.Length == 1 && ends.Length == 1, "The page has one block of this kind.");
        Assert.Equal(start, lines[starts[0]]);
        Assert.True(ends[0] > starts[0], "The block's end marker follows its start marker.");
        return string.Join('\n', lines[(starts[0] + 1)..ends[0]]) + "\n";
    }

    // A number standing on its own, not part of a name such as p99: 16, 5,808, 0.114 or 91%.
    [GeneratedRegex(@"(?<![\w.])\d+(?:[.,]\d+)*%?", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Number();

    [GeneratedRegex(@"^\[[^\]]+\]:\s", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReferenceDefinition();

    // An inline link's target, "](...)", or a reference link's label, "][...]".
    [GeneratedRegex(@"\](?:\([^)]*\)|\[[^\]]*\])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"#\d+\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IssueNumber();

    private static string RunMerge(params string[] arguments)
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
        foreach (var argument in arguments)
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
