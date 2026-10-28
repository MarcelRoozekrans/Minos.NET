using System.Globalization;
using System.Text;

namespace Minos.Evaluation.Tests;

public sealed class ResultsFileTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 30, 15, 123, TimeSpan.Zero);

    private static RunHeader Header(int count, string datasetName = "banking77-é", string? providerName = "openai", string? defaultModel = "gpt-x", int parallelism = 4)
        => new()
        {
            DatasetName = datasetName,
            ItemCount = count,
            ProviderName = providerName,
            Endpoint = new Uri("https://api.example.com/v1"),
            DefaultModel = defaultModel,
            Started = Start,
            Finished = Start.AddSeconds(90.5),
            Parallelism = parallelism,
        };

    private static EvaluationResults Sample()
    {
        var items = new List<ItemResult>
        {
            new()
            {
                Id = "a",
                Line = 1,
                LatencyMs = 1234.5678,
                Model = "gpt-x-2026",
                InputTokens = 100,
                OutputTokens = 20,
                Cost = 0.0012,
                ResponseId = "resp-1",
                Answers = new Dictionary<string, AnswerRecord>
                {
                    ["urgent"] = AnswerRecord.ForNoul(0.8123456789),
                    ["dépt"] = AnswerRecord.ForChoice("facturé-日本", 0.55),
                    ["mood"] = AnswerRecord.ForScore(2, 0.9),
                },
            },
            new() { Id = "b", Line = 2, LatencyMs = 10, Cached = true, Answers = new Dictionary<string, AnswerRecord> { ["urgent"] = AnswerRecord.ForNoul(0.25) } },
            new() { Id = "c", Line = 3, LatencyMs = 5.5, Error = new ItemError { Kind = "Http", StatusCode = 429, Message = "Té limit \"reached\"\nretry" } },
            new()
            {
                Id = "d",
                Line = 4,
                LatencyMs = 7,
                Model = "m",
                InputTokens = 1,
                OutputTokens = 2,
                Cost = 0,
                ResponseId = "r",
                Error = new ItemError { Kind = "Cancelled", Message = "stopped" },
            },
        };
        return new EvaluationResults(Header(items.Count), items);
    }

    internal static void AssertSameHeader(RunHeader expected, RunHeader actual)
    {
        Assert.Equal(expected.DatasetName, actual.DatasetName);
        Assert.Equal(expected.ItemCount, actual.ItemCount);
        Assert.Equal(expected.ProviderName, actual.ProviderName);
        Assert.Equal(expected.Endpoint, actual.Endpoint);
        Assert.Equal(expected.DefaultModel, actual.DefaultModel);
        Assert.Equal(expected.Started, actual.Started);
        Assert.Equal(expected.Finished, actual.Finished);
        Assert.Equal(expected.Parallelism, actual.Parallelism);
    }

    internal static void AssertSameAnswer(AnswerRecord expected, AnswerRecord actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Noul, actual.Noul);
        Assert.Equal(expected.Probability, actual.Probability);
        Assert.Equal(expected.ChoiceKey, actual.ChoiceKey);
        Assert.Equal(expected.ScoreLevel, actual.ScoreLevel);
        Assert.Equal(expected.Confidence, actual.Confidence);
        Assert.Equal(expected.ProbabilitySource, actual.ProbabilitySource);
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
        AssertSameHeader(expected.Header, actual.Header);
        Assert.Equal(expected.Items.Count, actual.Items.Count);
        for (var i = 0; i < expected.Items.Count; i++)
        {
            var e = expected.Items[i];
            var a = actual.Items[i];
            Assert.Equal(
                (e.Id, e.Line, e.LatencyMs, e.Cached, e.Model, e.InputTokens, e.OutputTokens, e.Cost, e.ResponseId),
                (a.Id, a.Line, a.LatencyMs, a.Cached, a.Model, a.InputTokens, a.OutputTokens, a.Cost, a.ResponseId));
            Assert.Equal((e.Error?.Kind, e.Error?.StatusCode, e.Error?.Message), (a.Error?.Kind, a.Error?.StatusCode, a.Error?.Message));
            Assert.Equal(e.Error is null, a.Error is null);
            Assert.Equal(e.Answers is null, a.Answers is null);
            if (e.Answers is not null)
            {
                Assert.Equal(e.Answers.Count, a.Answers!.Count);
                foreach (var (key, answer) in e.Answers)
                {
                    AssertSameAnswer(answer, a.Answers[key]);
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

    // A good item by default; each argument replaces one member.
    private static ItemResult Item(
        string id = "x1",
        int line = 1,
        double latency = 1,
        double? cost = null,
        string? model = null,
        string? responseId = null,
        int? inputTokens = null,
        int? outputTokens = null,
        IReadOnlyDictionary<string, AnswerRecord>? answers = null,
        bool noAnswers = false,
        ItemError? error = null)
        => new()
        {
            Id = id,
            Line = line,
            LatencyMs = latency,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Cost = cost,
            ResponseId = responseId,
            Answers = noAnswers ? null : answers ?? new Dictionary<string, AnswerRecord> { ["q"] = AnswerRecord.ForNoul(0.8) },
            Error = error,
        };

    private static ItemResult Ok(AnswerRecord? answer = null, double latency = 1, double? cost = null)
        => Item(latency: latency, cost: cost, answers: answer is null ? null : new Dictionary<string, AnswerRecord> { ["q"] = answer });

    private static ItemError Error(string kind = "Http", string message = "m") => new() { Kind = kind, Message = message };

    [Fact]
    public void Save_RefusesRecordsTheFileCannotHold()
    {
        AssertSaveRefuses(With(Item(error: Error())), "x1");
        AssertSaveRefuses(With(Item(noAnswers: true)), "x1");
        AssertSaveRefuses(With(Ok(latency: double.NaN)), "LatencyMs");
        AssertSaveRefuses(With(Ok(cost: double.PositiveInfinity)), "Cost");
        AssertSaveRefuses(With(Ok(cost: -1)), "Cost");
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
    public void UnknownMembers_AreProblems()
    {
        // A typo must not read as a missing optional member: "cots" is not an unknown cost.
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(body: ",\"cots\":0.1"), 2, "Unknown member 'cots'");
        AssertProblemOnLine(HeaderLine().Replace("\"parallelism\":1", "\"parallelism\":1,\"extra\":true", StringComparison.Ordinal), 1, "Unknown member 'extra'");
        AssertProblemOnLine(
            HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"score\",\"value\":1,\"confidence\":0.5,\"confidense\":0.5}}"),
            2,
            "Unknown member 'answers.q.confidense'");
        AssertProblemOnLine(
            HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"error\":{\"kind\":\"Http\",\"statusCode\":null,\"message\":\"x\",\"code\":1}"),
            2,
            "Unknown member 'error.code'");
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

    [Fact]
    public void Save_RefusesWhatLoadWouldRefuse()
    {
        var good = Ok();
        AssertSaveRefuses(new EvaluationResults(Header(2), [good]), "ItemCount");
        AssertSaveRefuses(new EvaluationResults(Header(1, datasetName: null!), [good]), "DatasetName");
        AssertSaveRefuses(new EvaluationResults(Header(1, datasetName: string.Empty), [good]), "DatasetName");
        AssertSaveRefuses(new EvaluationResults(Header(1, parallelism: 0), [good]), "Parallelism");
        AssertSaveRefuses(new EvaluationResults(Header(2), [good, good]), "twice");
        AssertSaveRefuses(With(Item(line: 0)), "Line");
        AssertSaveRefuses(With(Item(inputTokens: -1)), "InputTokens");
        AssertSaveRefuses(With(Item(outputTokens: -1)), "OutputTokens");
        AssertSaveRefuses(With(Item(answers: new Dictionary<string, AnswerRecord>())), "Answers");
    }

    [Theory]
    [InlineData(true, 0.2, 0.8)]
    [InlineData(true, 0.8, 0.7)]
    [InlineData(false, 0.5, 0.5)]
    [InlineData(false, 0.3, 0.3)]
    public void NoulDisagreeingWithItsProbability_IsAProblem(bool value, double probability, double confidence)
    {
        var answer = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"kind":"noul","value":{{(value ? "true" : "false")}},"probability":{{probability}},"confidence":{{confidence}}}""");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":" + answer + "}"), 2, "'q'");
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    public void RunnerNoulAnswer_SavesAndLoads(double p)
    {
        var answer = AnswerRecord.ForNoul(p);
        var results = With(Ok(answer));

        var loaded = Parse(Bytes(results));

        AssertSameAnswer(answer, loaded.Items[0].Answers!["q"]);
    }

    [Fact]
    public void ProbabilityOnAChoiceOrScoreAnswer_IsAProblem()
    {
        // An AnswerRecord holds a probability only for a Noul answer, so a file that gives one elsewhere cannot load as written.
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"choice\",\"value\":\"k\",\"confidence\":0.5,\"probability\":0.5}}"), 2, "probability");
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":{\"kind\":\"score\",\"value\":1,\"confidence\":0.5,\"probability\":0.5}}"), 2, "probability");
    }

    [Fact]
    public void Save_RefusesNullsThatLoadWouldRefuse()
    {
        AssertSaveRefuses(With(Item(id: null!)), "Id must be set");
        AssertSaveRefuses(With(Item(noAnswers: true, error: new ItemError { Kind = null!, Message = "m" })), "Error.Kind");
        AssertSaveRefuses(With(Item(noAnswers: true, error: new ItemError { Kind = "Http", Message = null! })), "Error.Message");
        AssertSaveRefuses(new EvaluationResults(Header(1), [null!]), "is null");
        AssertSaveRefuses(With(Item(answers: new Dictionary<string, AnswerRecord> { ["q"] = null! })), "Answers[q]");
    }

    [Fact]
    public void Save_ListsEveryProblem()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => With(Item(latency: double.NaN, cost: -1, line: 0)).Save(new MemoryStream()));

        Assert.Contains("LatencyMs", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Cost", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Line", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_ToAStream_WritesNothingWhenRefused()
    {
        using var stream = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => With(Item(answers: new Dictionary<string, AnswerRecord> { ["q"] = null! })).Save(stream));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void EmptyDatasetName_IsAProblem()
        => AssertProblemOnLine(HeaderLine().Replace("\"dataset\":\"d\"", "\"dataset\":\"\"", StringComparison.Ordinal), 1, "dataset");

    [Fact]
    public void Save_RefusesTextWithAnUnpairedSurrogate()
    {
        var lone = new string((char)0xD800, 1);
        var good = Ok();
        AssertSaveRefuses(new EvaluationResults(Header(1, datasetName: lone), [good]), "DatasetName");
        AssertSaveRefuses(new EvaluationResults(Header(1, providerName: lone), [good]), "ProviderName");
        AssertSaveRefuses(new EvaluationResults(Header(1, defaultModel: lone), [good]), "DefaultModel");
        AssertSaveRefuses(With(Item(id: lone)), "Id");
        AssertSaveRefuses(With(Item(model: lone)), "Model");
        AssertSaveRefuses(With(Item(responseId: lone)), "ResponseId");
        AssertSaveRefuses(With(Ok(AnswerRecord.ForChoice(lone, 0.5))), "ChoiceKey");
        AssertSaveRefuses(With(Item(answers: new Dictionary<string, AnswerRecord> { [lone] = good.Answers!["q"] })), "key");
        AssertSaveRefuses(With(Item(noAnswers: true, error: Error(message: "bad " + lone))), "Error.Message");
        AssertSaveRefuses(With(Item(noAnswers: true, error: Error(kind: lone))), "Error.Kind");
    }

    [Fact]
    public void PartialHeader_SavesAndLoads()
    {
        var header = new RunHeader
        {
            DatasetName = "d",
            ItemCount = 1,
            Started = Start,
            Finished = Start.AddSeconds(1),
            Parallelism = 1,
            Partial = true,
        };
        var results = new EvaluationResults(header, [new ItemResult { Id = "a", Line = 1, LatencyMs = 1, Answers = new Dictionary<string, AnswerRecord> { ["q"] = AnswerRecord.ForNoul(0.5) } }]);

        var text = Encoding.UTF8.GetString(Bytes(results));

        Assert.Contains("\"partial\":true", text, StringComparison.Ordinal);
        Assert.True(Parse(Bytes(results)).Header.Partial);
    }

    [Fact]
    public void NonPartialHeader_HasNoPartialMember_AndLoadsAsFalse()
    {
        var results = new EvaluationResults(Header(0), []);

        var text = Encoding.UTF8.GetString(Bytes(results));

        Assert.DoesNotContain("partial", text, StringComparison.Ordinal);
        Assert.False(Parse(Bytes(results)).Header.Partial);
    }

    // The bytes 1.5.0 writes for Sample(), whose answers are all Model: a file without a marked answer must not change.
    private const string ModelOnlySample =
        """{"kind":"minos-run","version":1,"dataset":"banking77-é","itemCount":4,"providerName":"openai","endpoint":"https://api.example.com/v1","defaultModel":"gpt-x","started":"2026-10-17T09:30:15.123+00:00","finished":"2026-10-17T09:31:45.623+00:00","parallelism":4}""" + "\n"
        + """{"id":"a","line":1,"latencyMs":1234.5678,"cached":false,"model":"gpt-x-2026","inputTokens":100,"outputTokens":20,"cost":0.0012,"responseId":"resp-1","answers":{"urgent":{"kind":"noul","value":true,"confidence":0.8123456789,"probability":0.8123456789},"dépt":{"kind":"choice","value":"facturé-日本","confidence":0.55},"mood":{"kind":"score","value":2,"confidence":0.9}}}""" + "\n"
        + """{"id":"b","line":2,"latencyMs":10,"cached":true,"model":null,"inputTokens":null,"outputTokens":null,"cost":null,"responseId":null,"answers":{"urgent":{"kind":"noul","value":false,"confidence":0.75,"probability":0.25}}}""" + "\n"
        + """{"id":"c","line":3,"latencyMs":5.5,"cached":false,"model":null,"inputTokens":null,"outputTokens":null,"cost":null,"responseId":null,"error":{"kind":"Http","statusCode":429,"message":"Té limit \"reached\"\nretry"}}""" + "\n"
        + """{"id":"d","line":4,"latencyMs":7,"cached":false,"model":"m","inputTokens":1,"outputTokens":2,"cost":0,"responseId":"r","error":{"kind":"Cancelled","statusCode":null,"message":"stopped"}}""" + "\n";

    private static EvaluationResults Marked()
    {
        var answers = new Dictionary<string, AnswerRecord>
        {
            ["urgent"] = AnswerRecord.ForNoul(0.8, ProbabilitySource.Verbalized),
            ["dept"] = AnswerRecord.ForChoice("billing", 0.55, ProbabilitySource.Logprobs),
            ["mood"] = AnswerRecord.ForScore(2, 0.9, ProbabilitySource.Verbalized),
            ["plain"] = AnswerRecord.ForNoul(0.3),
        };
        return new EvaluationResults(Header(1), [new ItemResult { Id = "a", Line = 1, LatencyMs = 1, Answers = answers }]);
    }

    [Fact]
    public void ModelOnlyResults_AreByteIdenticalToVersion1()
        => Assert.Equal(ModelOnlySample, Encoding.UTF8.GetString(Bytes(Sample())));

    [Fact]
    public void MarkedAnswers_SaveTheirSourceAndLoadBack()
    {
        var results = Marked();

        var bytes = Bytes(results);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith("""{"kind":"minos-run","version":1,""", text, StringComparison.Ordinal);
        Assert.Contains("""{"kind":"noul","value":true,"confidence":0.8,"probability":0.8,"probabilitySource":"verbalized"}""", text, StringComparison.Ordinal);
        Assert.Contains("""{"kind":"choice","value":"billing","confidence":0.55,"probabilitySource":"logprobs"}""", text, StringComparison.Ordinal);
        Assert.Contains("""{"kind":"score","value":2,"confidence":0.9,"probabilitySource":"verbalized"}""", text, StringComparison.Ordinal);
        Assert.Contains("""{"kind":"noul","value":false,"confidence":0.7,"probability":0.3}""", text, StringComparison.Ordinal);
        var loaded = Parse(bytes);
        AssertEqual(results, loaded);
        Assert.Equal(ProbabilitySource.Model, loaded.Items[0].Answers!["plain"].ProbabilitySource);
    }

    [Fact]
    public void AnswerWithoutASource_LoadsAsModel()
    {
        var text = HeaderLine(count: 1) + "\n" + ItemLine() + "\n";

        var loaded = EvaluationResults.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.True(loaded.IsSuccess);
        Assert.Equal(ProbabilitySource.Model, loaded.Value.Items[0].Answers!["q"].ProbabilitySource);
    }

    [Fact]
    public void NullSource_LoadsAsModel()
    {
        var answer = "{\"kind\":\"score\",\"value\":1,\"confidence\":0.5,\"probabilitySource\":null}";
        var text = HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":" + answer + "}") + "\n";

        var loaded = EvaluationResults.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.True(loaded.IsSuccess);
        Assert.Equal(ProbabilitySource.Model, loaded.Value.Items[0].Answers!["q"].ProbabilitySource);
    }

    [Theory]
    [InlineData("\"model\"")]
    [InlineData("\"Verbalized\"")]
    [InlineData("\"guessed\"")]
    [InlineData("2")]
    [InlineData("true")]
    public void UnknownSource_IsAProblem(string source)
    {
        var answer = "{\"kind\":\"score\",\"value\":1,\"confidence\":0.5,\"probabilitySource\":" + source + "}";
        AssertProblemOnLine(HeaderLine(count: 1) + "\n" + ItemLine(tail: ",\"answers\":{\"q\":" + answer + "}"), 2, "Member 'answers.q.probabilitySource' must be");
    }
}
