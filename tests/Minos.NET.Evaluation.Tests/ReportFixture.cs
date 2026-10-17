namespace Minos.Evaluation.Tests;

// The hand-computed 14-item fixture of the Phase 8.2 brief, and helpers for building smaller sets.
internal static class ReportFixture
{
    internal static readonly double[] Confidences = [0.55, 0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.92, 0.95, 0.97, 0.99];

    internal static readonly bool[] Correct = [false, true, false, true, true, false, true, true, true, true, true, true];

    internal static readonly ModelPrice FixturePrice = Price("jev-1.14", 0.042, 0, new DateOnly(2026, 10, 17), "OpenRouter listing");

    internal static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    internal static string Id(int item) => "i" + item.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static string NoulLine(string id, bool? expected)
        => "{\"id\":\"" + id + "\",\"state\":\"s\",\"version\":1,\"questions\":{\"q\":{\"type\":\"noul\",\"instructions\":\"?\"}},\"expected\":{"
            + (expected is { } e ? "\"q\":" + (e ? "true" : "false") : string.Empty) + "}}";

    internal static DecisionDataset Dataset(params string[] lines)
    {
        var result = DecisionDataset.Parse(string.Join('\n', lines.Select(l => l.ReplaceLineEndings(" "))));
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join("; ", result.Error.Select(p => $"{p.Line}: {p.Message}")));
        return result.Value;
    }

    internal static ModelPrice Price(string model, double inputPerMillion, double outputPerMillion, DateOnly date, string source)
        => new() { Model = model, InputPerMillion = inputPerMillion, OutputPerMillion = outputPerMillion, Date = date, Source = source };

    // For a confidence c from 0.5 to 1, 1 - c is exact, and so is 1 - (1 - c), so the answer's confidence is c itself.
    internal static AnswerRecord Noul(bool answer, double confidence) => AnswerRecord.ForNoul(answer ? confidence : 1 - confidence);

    internal static ItemResult Answered(
        int item,
        IReadOnlyDictionary<string, AnswerRecord> answers,
        double latencyMs = 100,
        bool cached = false,
        string? model = null,
        int? inputTokens = null,
        int? outputTokens = null,
        double? cost = null,
        string? id = null)
        => new()
        {
            Id = id ?? Id(item),
            Line = item,
            LatencyMs = latencyMs,
            Cached = cached,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Cost = cost,
            Answers = answers,
        };

    internal static ItemResult Failed(int item, string kind = "Server", string? id = null)
        => new() { Id = id ?? Id(item), Line = item, LatencyMs = 50, Error = new ItemError { Kind = kind, StatusCode = 500, Message = "failed" } };

    internal static RunHeader Header(int itemCount)
        => new()
        {
            DatasetName = "fixture",
            ItemCount = itemCount,
            ProviderName = "fake",
            DefaultModel = "jev-1.14",
            Started = Start,
            Finished = Start.AddMinutes(1),
            Parallelism = 1,
        };

    internal static EvaluationResults Results(params ItemResult[] items) => new(Header(items.Length), items);

    internal static DecisionDataset FixtureDataset()
        => Dataset([.. Enumerable.Range(1, 14).Select(i => NoulLine(Id(i), i <= 8))]);

    internal static EvaluationResults FixtureResults()
    {
        var items = new List<ItemResult>();
        for (var i = 1; i <= 12; i++)
        {
            var expected = i <= 8;
            var answer = Correct[i - 1] ? expected : !expected;
            var answers = new Dictionary<string, AnswerRecord> { ["q"] = Noul(answer, Confidences[i - 1]) };
            var latency = i * 100.0;
            items.Add(i switch
            {
                <= 4 => Answered(i, answers, latency, cost: 0.001),
                <= 8 => Answered(i, answers, latency, model: "jev-1.14-20261014", inputTokens: 1000, outputTokens: 0),
                _ => Answered(i, answers, latency),
            });
        }

        items.Add(Failed(13));
        items.Add(Failed(14));
        return Results([.. items]);
    }

    // The 14 Noul items of the fixture plus three items that carry a Noul, a Choice and a Score question, for the snapshot.
    internal static CalibrationReport AllKindsReport()
    {
        const string Questions = "\"questions\":{\"n\":{\"type\":\"noul\",\"instructions\":\"?\"},"
            + "\"c\":{\"type\":\"choice\",\"instructions\":\"?\",\"criteria\":{\"one\":\"1\",\"two\":\"2\",\"three\":\"3\"}},"
            + "\"s\":{\"type\":\"score\",\"instructions\":\"?\",\"criteria\":[\"a\",\"b\",\"c\"]}},\"expected\":{\"n\":true,\"c\":\"two\",\"s\":2}}";
        var lines = Enumerable.Range(1, 14).Select(i => NoulLine(Id(i), i <= 8)).ToList();
        for (var i = 15; i <= 17; i++)
        {
            lines.Add("{\"id\":\"" + Id(i) + "\",\"state\":\"x\",\"version\":1," + Questions);
        }

        var results = FixtureResults();
        var extra = new[]
        {
            Answered(15, new Dictionary<string, AnswerRecord>
            {
                ["n"] = Noul(true, 0.9),
                ["c"] = AnswerRecord.ForChoice("two", 0.4),
                ["s"] = AnswerRecord.ForScore(2, 0.8),
            }, 1200),
            Answered(16, new Dictionary<string, AnswerRecord>
            {
                ["n"] = Noul(false, 0.6),
                ["c"] = AnswerRecord.ForChoice("one", 0.7),
                ["s"] = AnswerRecord.ForScore(1, 0.6),
            }, 1300),
            Failed(17, "Timeout"),
        };
        var all = new EvaluationResults(Header(17), [.. results.Items, .. extra]);
        return Report(all, Dataset([.. lines]), new PriceTable([FixturePrice]));
    }

    internal static CalibrationReport Report(EvaluationResults results, DecisionDataset dataset, PriceTable? prices = null)
    {
        var result = CalibrationReport.Create(results, dataset, prices);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join("; ", result.Error.Select(p => $"{p.Line}: {p.Message}")));
        return result.Value;
    }
}
