using System.Globalization;
using System.Text;

namespace Minos.Evaluation.Tests;

public sealed class ResultsFileTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 30, 15, 123, TimeSpan.Zero);

    private static RunHeader Header(int count) => new(
        "banking77-é", count, "openai", new Uri("https://api.example.com/v1"), "gpt-x", Start, Start.AddSeconds(90.5), 4);

    private static EvaluationResults Sample()
    {
        var items = new List<ItemResult>
        {
            new("a", 1, 1234.5678, false, "gpt-x-2026", 100, 20, 0.0012, "resp-1",
                new Dictionary<string, AnswerRecord>
                {
                    ["urgent"] = new(QuestionKind.Noul, true, 0.8123456789, null, 0, 0.8123456789),
                    ["dépt"] = new(QuestionKind.Choice, false, null, "facturé-日本", 0, 0.55),
                    ["mood"] = new(QuestionKind.Score, false, null, null, 2, 0.9),
                },
                null),
            new("b", 2, 10, true, null, null, null, null, null,
                new Dictionary<string, AnswerRecord> { ["urgent"] = new(QuestionKind.Noul, false, 0.25, null, 0, 0.75) },
                null),
            new("c", 3, 5.5, false, null, null, null, null, null, null, new ItemError("Http", 429, "Té limit \"reached\"\nretry")),
            new("d", 4, 7, false, "m", 1, 2, 0, "r", null, new ItemError("Cancelled", null, "stopped")),
        };
        return new EvaluationResults(Header(items.Count), items);
    }

    private static byte[] Bytes(EvaluationResults results)
    {
        using var stream = new MemoryStream();
        results.Save(stream);
        return stream.ToArray();
    }

    private static EvaluationResults Parse(byte[] bytes)
    {
        var result = EvaluationResults.Parse(new MemoryStream(bytes));
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join("; ", result.Error.Select(p => $"{p.Line}: {p.Message}")));
        return result.Value;
    }

    private static void AssertEqual(EvaluationResults expected, EvaluationResults actual)
    {
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Items.Count, actual.Items.Count);
        for (var i = 0; i < expected.Items.Count; i++)
        {
            var e = expected.Items[i];
            var a = actual.Items[i];
            Assert.Equal(e with { Answers = null }, a with { Answers = null });
            Assert.Equal(e.Answers is null, a.Answers is null);
            if (e.Answers is not null)
            {
                Assert.Equal(e.Answers.Count, a.Answers!.Count);
                foreach (var (key, answer) in e.Answers)
                {
                    Assert.Equal(answer, a.Answers[key]);
                }
            }
        }
    }

    private static DatasetProblems Problems(string text)
    {
        var result = EvaluationResults.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        Assert.False(result.IsSuccess);
        return result.Error;
    }

    private static string HeaderLine(string kind = "minos-run", int version = 1, int count = 0)
        => $$"""{"kind":"{{kind}}","version":{{version}},"dataset":"d","itemCount":{{count}},"providerName":null,"endpoint":null,"defaultModel":null,"started":"2026-10-17T09:30:15Z","finished":"2026-10-17T09:31:15Z","parallelism":1}""";

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var results = Sample();
        AssertEqual(results, Parse(Bytes(results)));

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            results.Save(path);
            var loaded = EvaluationResults.Load(path);
            Assert.True(loaded.IsSuccess);
            AssertEqual(results, loaded.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_WritesLfLinesAndNoBom()
    {
        var bytes = Bytes(Sample());
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Equal(5, text.Count(c => c == '\n'));
        Assert.StartsWith("""{"kind":"minos-run","version":1,""", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderKindAndVersion_AreChecked()
    {
        var kind = Problems(HeaderLine(kind: "minos-report") + "\n");
        Assert.Contains(kind, p => p.Line == 1 && p.Message.Contains("minos-report", StringComparison.Ordinal));

        var version = Problems(HeaderLine(version: 2) + "\n");
        Assert.Contains(version, p => p.Line == 1 && p.Message.Contains("version", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedItemLine_IsALineNumberedProblem()
    {
        var text = HeaderLine(count: 2) + "\n{\"id\":\"a\"\n" + """{"id":"b","line":2,"latencyMs":1,"cached":false,"error":{"kind":"Http","statusCode":null,"message":"x"}}""" + "\n";
        var problems = Problems(text);
        Assert.Contains(problems, p => p.Line == 2);
        Assert.DoesNotContain(problems, p => p.Line == 3);
    }

    [Fact]
    public void ItemCountMismatchWithHeader_IsAProblem()
    {
        var text = HeaderLine(count: 3) + "\n"
            + """{"id":"b","line":2,"latencyMs":1,"cached":false,"error":{"kind":"Http","statusCode":null,"message":"x"}}""" + "\n";
        Assert.Contains(Problems(text), p => p.Line == 1 && p.Message.Contains('3', StringComparison.Ordinal));
    }

    [Fact]
    public void RoundTripsUnderAnotherCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            var results = Sample();
            var bytes = Bytes(results);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.Contains("1234.5678", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1234,5678", text, StringComparison.Ordinal);
            var loaded = Parse(bytes);
            AssertEqual(results, loaded);
            Assert.Equal(1234.5678, loaded.Items[0].LatencyMs);
            Assert.Equal(Start, loaded.Header.Started);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    private static string ItemLine(string body = "", string id = "a", string tail = ",\"answers\":{\"q\":{\"kind\":\"noul\",\"value\":true,\"probability\":0.8,\"confidence\":0.8}}")
        => "{\"id\":\"" + id + "\",\"line\":1,\"latencyMs\":1,\"cached\":false" + body + tail + "}";

    private static void AssertProblemOnLine(string text, int line, string? contains = null)
        => Assert.Contains(Problems(text), p => p.Line == line && (contains is null || p.Message.Contains(contains, StringComparison.Ordinal)));

    private static void AssertSaveRefuses(EvaluationResults results, string contains)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllText(path, "keep");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => results.Save(path));
            Assert.Contains(contains, ex.Message, StringComparison.Ordinal);
            Assert.Equal("keep", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".*.tmp"));
            Assert.Throws<InvalidOperationException>(() => results.Save(new MemoryStream()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static EvaluationResults With(ItemResult item) => new(Header(1), [item]);

    private static ItemResult Ok(AnswerRecord? answer = null, double latency = 1, double? cost = null)
        => new("x1", 1, latency, false, null, null, null, cost, null,
            new Dictionary<string, AnswerRecord> { ["q"] = answer ?? new AnswerRecord(QuestionKind.Noul, true, 0.8, null, 0, 0.8) }, null);

    [Fact]
    public void Save_RefusesRecordsTheFileCannotHold()
    {
        var good = Ok();
        AssertSaveRefuses(With(good with { Error = new ItemError("Http", null, "m") }), "x1");
        AssertSaveRefuses(With(good with { Answers = null }), "x1");
        AssertSaveRefuses(With(Ok(new AnswerRecord(QuestionKind.Choice, false, null, null, 0, 0.5))), "ChoiceKey");
        AssertSaveRefuses(With(Ok(new AnswerRecord(QuestionKind.Noul, true, null, null, 0, 0.5))), "Probability");
        AssertSaveRefuses(With(Ok(latency: double.NaN)), "latencyMs");
        AssertSaveRefuses(With(Ok(cost: double.PositiveInfinity)), "cost");
        AssertSaveRefuses(With(Ok(cost: -1)), "cost");
        AssertSaveRefuses(With(Ok(new AnswerRecord(QuestionKind.Score, false, null, null, 1, double.NaN))), "Confidence");
        AssertSaveRefuses(With(Ok(new AnswerRecord(QuestionKind.Noul, true, double.NegativeInfinity, null, 0, 0.5))), "Probability");
    }

    [Fact]
    public void Save_ToPath_ReplacesTheFileAndLeavesNoTemp()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            File.WriteAllText(path, "old");
            Sample().Save(path);
            Assert.True(EvaluationResults.Load(path).IsSuccess);
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".*.tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnswersAndErrorBothSet_OrNeither_AreProblems()
    {
        var err = ",\"error\":{\"kind\":\"Http\",\"statusCode\":null,\"message\":\"x\"}";
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"score\",\"value\":1,\"confidence\":0.5}}" + err), 2, "Exactly one");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: string.Empty), 2, "Exactly one");
    }

    [Fact]
    public void UnknownAnswerKindAndWrongTypedMember_AreProblems()
    {
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"rank\",\"value\":1,\"confidence\":0.5}}"), 2, "rank");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(body: string.Empty).Replace("\"latencyMs\":1", "\"latencyMs\":\"fast\"", StringComparison.Ordinal), 2, "latencyMs");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"choice\",\"value\":3,\"confidence\":0.5}}"), 2, "value");
    }

    [Theory]
    [InlineData(1.5, 0.8, "confidence")]
    [InlineData(-0.1, 0.8, "confidence")]
    [InlineData(0.8, 2.0, "probability")]
    public void ConfidenceAndProbabilityOutsideUnitRange_AreProblems(double confidence, double probability, string member)
    {
        var answer = string.Create(CultureInfo.InvariantCulture, $$"""{"kind":"noul","value":true,"probability":{{probability}},"confidence":{{confidence}}}""");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":" + answer + "}"), 2, member);
    }

    [Fact]
    public void ScoreAndCountsAndCost_NegativeValuesAreProblems()
    {
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"score\",\"value\":-1,\"confidence\":0.5}}"), 2, "value");
        AssertProblemOnLine(HeaderLine(count: -1), 1, "itemCount");
        AssertProblemOnLine(HeaderLine().Replace("\"parallelism\":1", "\"parallelism\":-1", StringComparison.Ordinal), 1, "parallelism");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(body: ",\"cost\":-1"), 2, "cost");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(body: ",\"inputTokens\":-1"), 2, "inputTokens");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(body: ",\"outputTokens\":-1"), 2, "outputTokens");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine().Replace("\"latencyMs\":1", "\"latencyMs\":-1", StringComparison.Ordinal), 2, "latencyMs");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine().Replace("\"line\":1", "\"line\":-1", StringComparison.Ordinal), 2, "line");
    }

    [Fact]
    public void EmptyAnswers_NoulWithoutProbability_AndDuplicateIds_AreProblems()
    {
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{}"), 2, "empty");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"noul\",\"value\":true,\"confidence\":0.8}}"), 2, "probability");
        AssertProblemOnLine(HeaderLine(count: 2) + "\n" + ItemLine() + "\n" + ItemLine(), 3, "twice");
    }

    [Fact]
    public void DuplicateMembers_AreProblems()
    {
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(body: ",\"cached\":true"), 2, "cached");
        AssertProblemOnLine(HeaderLine().Replace("\"parallelism\":1", "\"parallelism\":1,\"parallelism\":2", StringComparison.Ordinal), 1, "parallelism");
    }

    [Fact]
    public void TimestampWithoutOffset_IsAProblem()
    {
        AssertProblemOnLine(HeaderLine().Replace("2026-10-17T09:30:15Z", "2026-10-17T09:30:15", StringComparison.Ordinal), 1, "started");
    }

    [Fact]
    public void CountMismatch_IsReportedEvenWithAMalformedLine()
    {
        var text = HeaderLine(count: 3) + "\n{\"id\":\n" + ItemLine(id: "b") + "\n";
        var problems = Problems(text);
        Assert.Contains(problems, p => p.Line == 2);
        Assert.Contains(problems, p => p.Line == 1 && p.Message.Contains("header says 3", StringComparison.Ordinal));
    }
}
