using System.Globalization;
using System.Text.Json;

namespace Minos.Docs.Tests;

// Ties every figure of "Choosing a provider and a threshold" to the committed calibration reports. Each table has one
// row per report folder, and each cell is formatted from that folder's report.json the way the report's own Markdown
// formats it, so a table that drifts from its report, a folder with no row or a row with no folder fails here.
public sealed class ProviderGuideTests
{
    private const string Page = "choosing-a-provider.md";
    private const string ReportsUrl = "https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/";
    private const string LicenseUrl = "https://github.com/MarcelRoozekrans/Minos.NET/blob/main/datasets/banking77/LICENSE";
    private const string DatasetReadmeUrl = "https://github.com/MarcelRoozekrans/Minos.NET/blob/main/datasets/banking77/README.md";
    private const double Z = 1.96;

    private static readonly string ReportsRoot = Path.Combine(PublishedPages.Root, "docs", "reports");

    // A comparison table per question kind: its heading, the set it reads and the report's per-kind block. The overall
    // block pools the kinds, so no table reads it.
    private static readonly (string Heading, string Set, string Kind)[] KindTables =
    [
        ("Banking77 intent (Choice)", "banking77", "choice"),
        ("Banking77 yes-or-no questions (Noul)", "banking77", "noul"),
        ("Urgency (Score)", "urgency", "score"),
    ];

    // A threshold sweep table per kind, under "Choosing a threshold".
    private static readonly (string Heading, string Set, string Kind)[] SweepTables =
    [
        ("Banking77 intent sweep", "banking77", "choice"),
        ("Banking77 yes-or-no questions sweep", "banking77", "noul"),
        ("Urgency sweep", "urgency", "score"),
    ];

    private const string CostTable = "Cost and speed";

    // The columns of the cost table, after Provider: the set each reads and what it shows.
    private static readonly (string Header, string Set, bool Cost)[] CostColumns =
    [
        ("Banking77 cost per 1,000", "banking77", true),
        ("Banking77 median latency", "banking77", false),
        ("Urgency cost per 1,000", "urgency", true),
        ("Urgency median latency", "urgency", false),
    ];

    private const string IntervalTable = "Intervals";

    // The columns of the interval table, after Provider: the set and kind each reads.
    private static readonly (string Header, string Set, string Kind)[] IntervalColumns =
    [
        ("Banking77 intent", "banking77", "choice"),
        ("Banking77 yes-or-no", "banking77", "noul"),
        ("Urgency", "urgency", "score"),
    ];

    [Fact]
    public void EachTable_HasOneRowPerReportFolder()
    {
        var problems = new List<string>();
        foreach (var (heading, set, _) in KindTables.Concat(SweepTables))
        {
            Compare(heading, Folders(PageTables.Rows(Page, heading)), ReportFolders(set), problems);
        }

        foreach (var heading in new[] { CostTable, IntervalTable })
        {
            Compare(heading, Folders(PageTables.Rows(Page, heading)), ProviderFolders(), problems);
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void EachCell_MatchesItsReport()
    {
        var problems = new List<string>();
        foreach (var (heading, _, kind) in KindTables)
        {
            var header = PageTables.Header(Page, heading);
            var threshold = ReferenceThreshold(heading, header);
            Assert.Equal(
                ["Provider", "Model", "Items", "Answered", "Errors", "Accuracy", "ECE", "Bins", "Coverage at " + Threshold(threshold), "Accuracy at " + Threshold(threshold)],
                header);

            foreach (var row in PageTables.Rows(Page, heading))
            {
                var folder = Folder(row[0]);
                using var report = Report(folder);
                var root = report.RootElement;
                var block = root.GetProperty(kind);
                var point = SweepPoint(block, threshold);
                string[] expected =
                [
                    row[0],
                    Model(root),
                    Count(root.GetProperty("header").GetProperty("itemCount").GetInt32()),
                    Count(block.GetProperty("answered").GetInt32()) + " of " + Count(block.GetProperty("scored").GetInt32()),
                    Count(root.GetProperty("errors").GetProperty("count").GetInt32()),
                    Ratio(block.GetProperty("accuracy")),
                    Ratio(block.GetProperty("ece")),
                    Bins(block.GetProperty("bins")),
                    Ratio(point.GetProperty("coverage")) + " (" + Count(point.GetProperty("covered").GetInt32()) + ")",
                    Ratio(point.GetProperty("accuracy")),
                ];
                CompareRow(heading + ", " + folder, header, expected, row, problems);
            }
        }

        var costHeader = PageTables.Header(Page, CostTable);
        Assert.Equal(["Provider", .. CostColumns.Select(c => c.Header)], costHeader);
        foreach (var row in PageTables.Rows(Page, CostTable))
        {
            var provider = Folder(row[0]);
            var expected = new List<string> { row[0] };
            foreach (var (_, set, cost) in CostColumns)
            {
                using var report = Report(provider + "/" + set);
                var root = report.RootElement;
                expected.Add(cost
                    ? Cost(root.GetProperty("cost").GetProperty("perThousandDecisions"))
                    : Millis(root.GetProperty("latency").GetProperty("medianMs")));
            }

            CompareRow(CostTable + ", " + provider, costHeader, [.. expected], row, problems);
        }

        foreach (var (heading, _, kind) in SweepTables)
        {
            var header = PageTables.Header(Page, heading);
            Assert.Equal("Provider", header[0]);
            var thresholds = header[1..].Select(h => double.Parse(h, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(header[1..], thresholds.Select(Threshold));
            foreach (var row in PageTables.Rows(Page, heading))
            {
                var folder = Folder(row[0]);
                using var report = Report(folder);
                var block = report.RootElement.GetProperty(kind);
                var expected = new List<string> { row[0] };
                foreach (var threshold in thresholds)
                {
                    var point = SweepPoint(block, threshold);
                    expected.Add(Ratio(point.GetProperty("coverage")) + " / " + Ratio(point.GetProperty("accuracy")));
                }

                CompareRow(heading + ", " + folder, header, [.. expected], row, problems);
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void EachInterval_MatchesTheWilsonFormula()
    {
        var header = PageTables.Header(Page, IntervalTable);
        Assert.Equal(["Provider", .. IntervalColumns.Select(c => c.Header)], header);

        var problems = new List<string>();
        foreach (var row in PageTables.Rows(Page, IntervalTable))
        {
            var provider = Folder(row[0]);
            var expected = new List<string> { row[0] };
            foreach (var (_, set, kind) in IntervalColumns)
            {
                using var report = Report(provider + "/" + set);
                var block = report.RootElement.GetProperty(kind);
                var answered = block.GetProperty("answered").GetInt32();
                var exact = block.GetProperty("accuracy").GetDouble() * answered;
                var correct = (int)Math.Round(exact);
                Assert.True(Math.Abs(exact - correct) < 1e-6, $"{provider}/{set} {kind}: accuracy times answered, {exact}, is not a whole count.");

                var (low, high) = Wilson(correct, answered);
                expected.Add(Count(correct) + " of " + Count(answered) + ", " + Fixed(low) + " to " + Fixed(high));
            }

            CompareRow(IntervalTable + ", " + provider, header, [.. expected], row, problems);
        }

        Assert.Empty(problems);
    }

    // The closed forms at the edges: all right gives n / (n + z^2) to 1, none right gives 0 to z^2 / (n + z^2).
    [Fact]
    public void Wilson_MatchesItsClosedFormsAtTheEdges()
    {
        var (low, high) = Wilson(60, 60);
        Assert.Equal(60 / (60 + (Z * Z)), low, 12);
        Assert.Equal(1, high, 12);

        (low, high) = Wilson(0, 400);
        Assert.Equal(0, low, 12);
        Assert.Equal(Z * Z / (400 + (Z * Z)), high, 12);
    }

    // The page says when the runs were made, and that is the day every report's run started.
    [Fact]
    public void TheGuide_StatesTheDayTheRunsWereMade()
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            PageTables.Text(Page), @"measured on (?<day>\d{4}-\d{2}-\d{2})", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(match.Success, $"{Page} says 'measured on <yyyy-mm-dd>'.");

        var folders = ReportFolders("banking77").Concat(ReportFolders("urgency")).ToList();
        Assert.NotEmpty(folders);
        foreach (var folder in folders)
        {
            using var report = Report(folder);
            var started = report.RootElement.GetProperty("header").GetProperty("started").GetString()!;
            Assert.Equal(match.Groups["day"].Value, started[..10]);
        }
    }

    [Fact]
    public void TheGuide_CreditsPolyAI_AndTheLicense()
    {
        var text = PageTables.Text(Page);
        var targets = Markdown.Links(File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", Page))).Select(l => l.Target).ToList();

        Assert.Contains("PolyAI", text, StringComparison.Ordinal);
        Assert.Contains("CC BY 4.0", text, StringComparison.Ordinal);
        Assert.Contains(LicenseUrl, targets);
        Assert.Contains(DatasetReadmeUrl, targets);
        Assert.True(File.Exists(Path.Combine(PublishedPages.Root, "datasets", "banking77", "LICENSE")));
    }

    // The local model's cost of 0 and every latency need their note, right under the table: the local model costs
    // hardware, and each run's latency reflects the GitHub-hosted runner it ran from, a CPU-only one for the local model.
    [Fact]
    public void TheCostTable_IsFollowedByItsNotes()
    {
        var notes = NotesAfter(CostTable);

        Assert.Contains("hardware", notes, StringComparison.Ordinal);
        Assert.Contains("CPU", notes, StringComparison.Ordinal);
        Assert.Contains("GitHub-hosted", notes, StringComparison.Ordinal);
    }

    // The prose between a table's heading and the next heading, without the table's rows.
    private static string NotesAfter(string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", Page));
        var start = Array.FindIndex(lines, line => line.StartsWith('#') && string.Equals(line.TrimStart('#').Trim(), heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{Page} has a heading '{heading}'.");
        var section = lines[(start + 1)..].TakeWhile(line => !line.StartsWith('#'));
        return string.Join('\n', section.Where(line => !line.StartsWith('|')));
    }

    [Fact]
    public void ThePage_IsInTheReadme()
    {
        var readme = File.ReadAllText(Path.Combine(PublishedPages.Root, "README.md"));

        Assert.Contains("(https://marcelroozekrans.github.io/Minos.NET/choosing-a-provider)", readme, StringComparison.Ordinal);
    }

    // Both gates of the snippet are hit on both sides of each threshold.
    [Theory]
    [InlineData("card_arrival", 0.95, 2, 0.5, SupportRoute.OnCall)]
    [InlineData("card_arrival", 0.95, 2, 0.49, SupportRoute.Person)]
    [InlineData("card_arrival", 0.95, 2, 0.3, SupportRoute.Person)]
    [InlineData("card_arrival", 0.95, 2, 0.29, SupportRoute.TopicQueue)]
    [InlineData("lost_or_stolen_card", 0.9, 1, 0.99, SupportRoute.TopicQueue)]
    [InlineData("lost_or_stolen_card", 0.89, 0, 0.99, SupportRoute.ConfirmTopic)]
    [InlineData("declined_card_payment", 0.5, 1, 0.9, SupportRoute.ConfirmTopic)]
    [InlineData("declined_card_payment", 0.49, 1, 0.9, SupportRoute.Person)]
    public async Task EachGate_RoutesOnItsOwnThresholds(string topic, double topicConfidence, int urgency, double urgencyConfidence, SupportRoute expected)
    {
        var message = await CannedDecision.EvaluateAsync<SupportMessage>(Response(topic, topicConfidence, urgency, urgencyConfidence), "My card was stolen this morning.");

        Assert.Equal(expected, SupportGates.Route(message));
    }

    private static readonly string[] TopicKeys = ["card_arrival", "lost_or_stolen_card", "declined_card_payment"];

    // The chosen topic's probability equals its confidence and the others share the rest. The chosen urgency level
    // holds 0.8 of the probability and the other two 0.1 each, so it is the most probable level.
    private static string Response(string topic, double topicConfidence, int urgency, double urgencyConfidence)
    {
        var others = (1.0 - topicConfidence) / (TopicKeys.Length - 1);
        var topicProbabilities = string.Join(
            ',',
            TopicKeys.Select(key => string.Create(
                CultureInfo.InvariantCulture,
                $"\"{key}\":{(string.Equals(key, topic, StringComparison.Ordinal) ? topicConfidence : others)}")));
        var levels = Enumerable.Range(0, 3).Select(level => level == urgency ? 0.8 : 0.1).ToArray();
        var levelProbabilities = string.Join(',', levels.Select((p, i) => string.Create(CultureInfo.InvariantCulture, $"\"{i}\":{p}")));
        var expectedLevel = levels.Select((p, i) => p * i).Sum();

        return string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"jev-1.13.0","answers":{"topic":{"type":"choice","choice":"{{{topic}}}","probabilities":{{{{topicProbabilities}}}},"confidence":{{{topicConfidence}}}},"urgency":{"type":"score","score":{{{expectedLevel}}},"legend":{"0":"Routine: a question, with nothing at risk","1":"Needs action soon: the customer is blocked","2":"Urgent: money or card security at risk"},"probabilities":{{{{levelProbabilities}}}},"confidence":{{{urgencyConfidence}}}}},"usage":{"input_tokens":120,"output_tokens":16}}""");
    }

    // The threshold the comparison table is read at, from its "Coverage at t" column.
    private static double ReferenceThreshold(string heading, string[] header)
    {
        const string Prefix = "Coverage at ";
        var column = Array.Find(header, h => h.StartsWith(Prefix, StringComparison.Ordinal));
        Assert.True(column is not null, $"The table under '{heading}' has a '{Prefix}<t>' column.");
        return double.Parse(column[Prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static void Compare(string heading, List<string> rows, List<string> folders, List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!seen.Add(row))
            {
                problems.Add($"'{heading}' has more than one row for docs/reports/{row}.");
            }
        }

        problems.AddRange(folders.Except(rows, StringComparer.Ordinal).Select(f => $"'{heading}' has no row for docs/reports/{f}."));
        problems.AddRange(rows.Except(folders, StringComparer.Ordinal).Select(r => $"'{heading}' has a row for docs/reports/{r}, which is not a report folder."));
    }

    private static void CompareRow(string where, string[] header, string[] expected, string[] actual, List<string> problems)
    {
        if (actual.Length != expected.Length)
        {
            problems.Add($"{where}: {actual.Length} cells, expected {expected.Length}.");
            return;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                problems.Add($"{where}, {header[i]}: '{actual[i]}', the report gives '{expected[i]}'.");
            }
        }
    }

    private static List<string> Folders(string[][] rows) => [.. rows.Select(row => Folder(row[0]))];

    // The report folder a Provider cell links, relative to docs/reports/: "<provider>/<set>" or "<provider>".
    private static string Folder(string cell)
    {
        var links = Markdown.Links([cell]);
        Assert.True(links.Count == 1, $"'{cell}' links one report folder.");
        var target = links[0].Target;
        Assert.True(target.StartsWith(ReportsUrl, StringComparison.Ordinal), $"'{target}' starts with {ReportsUrl}.");
        return target[ReportsUrl.Length..];
    }

    private static List<string> ReportFolders(string set)
        => [.. Directory.GetDirectories(ReportsRoot)
            .Where(provider => File.Exists(Path.Combine(provider, set, "report.json")))
            .Select(provider => Path.GetFileName(provider) + "/" + set)
            .Order(StringComparer.Ordinal)];

    private static List<string> ProviderFolders()
        => [.. Directory.GetDirectories(ReportsRoot)
            .Where(provider => Directory.GetDirectories(provider).Any(set => File.Exists(Path.Combine(set, "report.json"))))
            .Select(Path.GetFileName)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)];

    private static JsonDocument Report(string folder)
    {
        var path = Path.Combine(ReportsRoot, folder, "report.json");
        Assert.True(File.Exists(path), $"docs/reports/{folder} has a report.json.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    // The model asked for, and the models that answered when they differ from it, as the report's header and its
    // "Responses served by" line name them.
    private static string Model(JsonElement root)
    {
        var requested = root.GetProperty("header").GetProperty("defaultModel").GetString()!;
        var served = root.GetProperty("responseModels").EnumerateArray().Select(m => m.GetProperty("model").GetString() ?? "no model named").ToList();
        return served.Count == 1 && string.Equals(served[0], requested, StringComparison.Ordinal)
            ? $"`{requested}`"
            : $"`{requested}`, served as " + string.Join(", ", served.Select(m => $"`{m}`"));
    }

    // Equal-mass bins: their number times the answers in each, or the smallest and largest when they differ by one.
    private static string Bins(JsonElement bins)
    {
        var counts = bins.EnumerateArray().Select(b => b.GetProperty("count").GetInt32()).ToList();
        var (min, max) = (counts.Min(), counts.Max());
        return Count(counts.Count) + " × " + (min == max ? Count(min) : Count(min) + "–" + Count(max));
    }

    private static JsonElement SweepPoint(JsonElement block, double threshold)
    {
        foreach (var point in block.GetProperty("sweep").EnumerateArray())
        {
            if (Math.Round(point.GetProperty("threshold").GetDouble() * 100) == Math.Round(threshold * 100))
            {
                return point;
            }
        }

        Assert.Fail($"The sweep has no point at {Threshold(threshold)}.");
        return default;
    }

    private static (double Low, double High) Wilson(int correct, int n)
    {
        var p = (double)correct / n;
        var z2 = Z * Z;
        var denominator = 1 + (z2 / n);
        var centre = (p + (z2 / (2 * n))) / denominator;
        var half = Z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4.0 * n * n))) / denominator;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    // The formats of the report's own Markdown, in ReportMarkdownWriter.
    private static string Ratio(JsonElement value) => value.ValueKind == JsonValueKind.Null ? "n/a" : Fixed(value.GetDouble());

    private static string Fixed(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    private static string Cost(JsonElement value) => value.ValueKind == JsonValueKind.Null ? "n/a" : value.GetDouble().ToString("G6", CultureInfo.InvariantCulture);

    private static string Millis(JsonElement value) => value.ValueKind == JsonValueKind.Null ? "n/a" : value.GetDouble().ToString("0.#", CultureInfo.InvariantCulture) + " ms";

    private static string Threshold(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
