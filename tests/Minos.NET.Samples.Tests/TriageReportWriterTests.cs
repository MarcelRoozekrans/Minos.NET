using System.Globalization;
using System.Text.RegularExpressions;
using Minos.Evaluation;
using Minos.Samples.TicketTriage;
using ZeroAlloc.TestHelpers;

namespace Minos.Samples.Tests;

public sealed partial class TriageReportWriterTests
{
    private static readonly string[] DatasetIds = ["a", "b"];

    private static readonly string[] ReportNames = ["triage-report.json", "triage-report.md"];

    private static readonly string[] Headings =
    [
        "# Triage report", "## Run", "## Split", "## Accuracy", "## Escalated questions", "## The signal", "## Latency", "## Cost", "## How to read this",
    ];

    // One fixed report: the Choice kind has 40 questions, the Noul kind 12 (small), All is their sum.
    private static TriageReport Fixed()
        => new(
            new RunSection(
                60,
                52,
                new Dictionary<string, int> { ["Timeout"] = 5, ["InvalidResponse"] = 3 },
                new Dictionary<string, int> { ["jev-1.14.0"] = 17, ["local-model"] = 52 }),
            new PerKind<SplitCounts>(new(30, 6, 2, 2, 8), new(8, 2, 1, 1, 3), new(38, 8, 3, 3, 10)),
            new PerKind<AccuracySection>(
                new(new(28, 40), new(31, 40), new(5, 6)),
                new(new(9, 12), new(10, 12), new(2, 2)),
                new(new(37, 52), new(41, 52), new(7, 8))),
            new PerKind<EscalatedTable>(new(3, 1, 2, 0), new(1, 0, 1, 0), new(4, 1, 3, 0)),
            new PerKind<SignalSection>(new(7, 3, 10, 2), new(2, 1, 4, 1), new(9, 4, 14, 3)),
            new LatencySection(new(10, 612.5, 1204), new(42, 88, 240), new(52, 80, 210), new(8, 520, 1100)),
            new CostSection(
                0.123456,
                10,
                0,
                0,
                [new ModelPrice { Model = "jev-1.14.0", InputPerMillion = 0.042, OutputPerMillion = 0.084, Date = new DateOnly(2026, 11, 1), Source = "the provider's price list" }]));

    [Fact]
    public void Markdown_HasTheSectionsInOrder()
    {
        var md = TriageReportMarkdownWriter.Write(Fixed());

        Assert.StartsWith("# Triage report\n", md, StringComparison.Ordinal);
        var last = 0;
        foreach (var heading in Headings.Skip(1))
        {
            var at = md.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
            Assert.True(at > last, heading);
            last = at;
        }
    }

    [Fact]
    public void Markdown_PrintsCountsNextToShares_AndMarksSmallCounts()
    {
        var md = TriageReportMarkdownWriter.Write(Fixed());

        Assert.Contains("- Questions: 40", md, StringComparison.Ordinal);
        Assert.Contains("- Confident, kept on the local answer: 30 of 40 (0.7500)\n", md, StringComparison.Ordinal);
        Assert.Contains("- Questions: 12 (small: n < 30)\n", md, StringComparison.Ordinal);
        Assert.Contains("- Confident, kept on the local answer: 8 of 12 (0.6667) (small: n < 30)\n", md, StringComparison.Ordinal);
        Assert.Contains("Decided: 52 of 60 (0.8667).\n", md.Replace(" (small: n < 30)", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("Prices used, per million tokens:", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_HasNoVerdictWords()
        => Assert.DoesNotMatch(VerdictWords(), TriageReportMarkdownWriter.Write(Fixed()));

    [Fact]
    public void Writers_AreCultureIndependent()
    {
        var invariantMd = TriageReportMarkdownWriter.Write(Fixed());
        var invariantJson = TriageReportJsonWriter.Write(Fixed());
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            var dutch = new CultureInfo("nl-NL");
            CultureInfo.CurrentCulture = dutch;
            CultureInfo.CurrentUICulture = dutch;

            Assert.Equal(invariantMd, TriageReportMarkdownWriter.Write(Fixed()));
            Assert.Equal(invariantJson, TriageReportJsonWriter.Write(Fixed()));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }

    [Fact]
    public void Json_IsStable_ForTheSameReport()
    {
        var first = TriageReportJsonWriter.Write(Fixed());
        var second = TriageReportJsonWriter.Write(Fixed());

        Assert.Equal(first, second);
        Assert.DoesNotContain('\r', first);
        Assert.EndsWith("}\n", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_ShowsUnknownRatesAndCostAsNotAvailable()
    {
        var empty = new TriageReport(
            new RunSection(0, 0, new Dictionary<string, int>(), new Dictionary<string, int>()),
            new PerKind<SplitCounts>(new(0, 0, 0, 0, 0), new(0, 0, 0, 0, 0), new(0, 0, 0, 0, 0)),
            new PerKind<AccuracySection>(Zero(), Zero(), Zero()),
            new PerKind<EscalatedTable>(new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0)),
            new PerKind<SignalSection>(new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0)),
            new LatencySection(new(0, null, null), new(0, null, null), new(0, null, null), new(0, null, null)),
            new CostSection(null, 0, 0, 0, []));

        var md = TriageReportMarkdownWriter.Write(empty);

        Assert.Contains("0 of 0 (n/a)", md, StringComparison.Ordinal);
        Assert.Contains("Per 1,000 tickets: n/a", md, StringComparison.Ordinal);
        Assert.Contains("Prices used: none.", md, StringComparison.Ordinal);
        Assert.Contains("null", TriageReportJsonWriter.Write(empty), StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_MatchesTheSnapshot() => TextSnapshot.VerifyText(TriageReportMarkdownWriter.Write(Fixed()), "md");

    [Fact]
    public void Json_MatchesTheSnapshot() => TextSnapshot.VerifyText(TriageReportJsonWriter.Write(Fixed()), "json");

    [Fact]
    public void ReportFiles_RebuildByteForByte()
    {
        using var dir = new TempDirectory();
        var inputs = dir.Inputs("a", "b");

        TriageReportFiles.Write(inputs.Records, inputs.Dataset, inputs.Prices, Path.Combine(dir.Path, "one"));
        TriageReportFiles.Write(inputs.Records, inputs.Dataset, inputs.Prices, Path.Combine(dir.Path, "two"));

        foreach (var name in ReportNames)
        {
            var first = File.ReadAllBytes(Path.Combine(dir.Path, "one", name));
            Assert.NotEmpty(first);
            Assert.Equal(first, File.ReadAllBytes(Path.Combine(dir.Path, "two", name)));
            Assert.DoesNotContain((byte)'\r', first);
        }
    }

    [Fact]
    public void ReportFiles_RejectAnUnknownId_NamingTheId()
    {
        using var dir = new TempDirectory();
        var inputs = dir.Inputs("a", "zz-unknown");

        var ex = Assert.Throws<InvalidDataException>(() => TriageReportFiles.Write(inputs.Records, inputs.Dataset, inputs.Prices, Path.Combine(dir.Path, "out")));

        Assert.Contains("zz-unknown", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportFiles_RejectADuplicatedId()
    {
        using var dir = new TempDirectory();
        var inputs = dir.Inputs("a", "a");

        var ex = Assert.Throws<InvalidDataException>(() => TriageReportFiles.Write(inputs.Records, inputs.Dataset, inputs.Prices, Path.Combine(dir.Path, "out")));

        Assert.Contains("'a'", ex.Message, StringComparison.Ordinal);
    }

    private static TriageRecord Record(string id)
    {
        var right = new TriageAnswer("choice", null, "a", 0.9);
        return new TriageRecord(
            id,
            12,
            null,
            [new TriageQuestion("intent", right, "local", "Confident", 0.9, null, right)],
            [new TriageCall("local", null, null, null, null, "Answered", 3)]);
    }

    private static AccuracySection Zero() => new(new(0, 0), new(0, 0), new(0, 0));

    [GeneratedRegex(@"\b(?:should|recommend\w*|adopt\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VerdictWords();

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public (string Records, string Dataset, string Prices) Inputs(params string[] recordIds)
        {
            var dataset = System.IO.Path.Combine(Path, "dataset.jsonl");
            File.WriteAllText(
                dataset,
                string.Join(
                    "\n",
                    DatasetIds.Select(static id =>
                        """{"id":"ITEM","state":"s","questions":{"intent":{"type":"choice","instructions":"i","criteria":{"a":{"description":"A"},"b":{"description":"B"}}}},"expected":{"intent":"a"}}""".Replace("ITEM", id, StringComparison.Ordinal))) + "\n");
            var records = System.IO.Path.Combine(Path, "records.jsonl");
            File.WriteAllText(records, string.Join("\n", recordIds.Select(static id => TriageRecordWriter.Line(Record(id)))) + "\n");
            var prices = System.IO.Path.Combine(Path, "prices.json");
            File.WriteAllText(prices, """{"prices":[]}""");
            return (records, dataset, prices);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
