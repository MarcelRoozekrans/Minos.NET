using System.Buffers;
using System.Text;
using System.Text.Json;
using Minos.Evaluation;
using Minos.Recording.RecordFormat;
using ZeroAlloc.TestHelpers;

namespace Minos.Recording.Tests;

public sealed class RecordFileTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 9, 30, 0, TimeSpan.FromHours(2));

    private readonly string _directory = Directory.CreateTempSubdirectory("minos-recording-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name = "decisions.jsonl") => Path.Combine(_directory, name);

    private static QuestionSetDefinition Definition() => new(
        QuestionDefinition.Noul("urgent", "Does the customer need action today?", whenTrue: "A deadline is mentioned.", whenFalse: "Nothing is time bound."),
        QuestionDefinition.Choice(
            "topic",
            "Which topic is this message about?",
            new OptionDefinition("card_arrival", Criterion.Text("The card has not arrived yet.").WithExamples("Where is my card?").WithNotFor("A card that does not work.")),
            new OptionDefinition("card_not_working", "The card arrived but does not work."),
            new OptionDefinition("lost_or_stolen_card", null)),
        QuestionDefinition.Score("mood", "How upset is the customer?", "calm", Criterion.Text("annoyed"), "angry"));

    private static IReadOnlyList<RecordedAnswer> Answers() =>
    [
        new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.25, 0, 0.5, []),
        new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.125, 0.125]),
        new RecordedAnswer("mood", QuestionKind.Score, 1, 0, 1.25, 0.625, [0.25, 0.5, 0.25]),
    ];

    private static DecisionRecord Record(
        string id = "d-1",
        string? erasureKey = "customer-7",
        DecisionContent? state = null,
        IReadOnlyList<RecordedAnswer>? answers = null,
        DecisionErrorKind? errorKind = null,
        IReadOnlyList<string>? refusedKeys = null,
        QuestionSetDefinition? definition = null)
        => new(
            id,
            erasureKey,
            T0,
            TimeSpan.FromTicks(2_503_210),
            "openai",
            "https://api.example.test/v1/",
            "model-default",
            "model-requested",
            "model-served-2026-10",
            "resp-1",
            "upstream-a",
            new DecisionUsage { InputTokens = 120, OutputTokens = 14, Cost = 0.000375 },
            isCached: false,
            definition ?? Definition(),
            state ?? DecisionContent.FromString("My card was meant to arrive last week and nothing has come."),
            imageCount: 1,
            ["9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"],
            answers ?? Answers(),
            errorKind,
            refusedKeys ?? []);

    private static byte[] Lines(params Action<Utf8JsonWriter>[] writes)
    {
        var buffer = new ArrayBufferWriter<byte>();
        foreach (var write in writes)
        {
            RecordLine.WriteTo(buffer, write);
            buffer.Write("\n"u8);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static Action<Utf8JsonWriter> Rec(DecisionRecord record) => w => RecordLine.Write(w, record);

    private static Action<Utf8JsonWriter> Out(string id, DecisionOutcome outcome) => w => RecordLine.WriteOutcome(w, id, outcome);

    private static Action<Utf8JsonWriter> Erase(string? key, string? id) => w => RecordLine.WriteErasure(w, key, id);

    private static DecisionOutcome Confirmed() => DecisionOutcome.Confirmed(
        "reviewer-ui",
        T0.AddMinutes(5),
        new Dictionary<string, ConfirmedAnswer>
        {
            ["urgent"] = ConfirmedAnswer.ForNoul(true),
            ["topic"] = ConfirmedAnswer.ForChoice("card_arrival"),
            ["mood"] = ConfirmedAnswer.ForScore(2),
        });

    private static DecisionOutcome Signal() => DecisionOutcome.Signal("mailer", T0.AddHours(1), "complained", 1, "Wrote back within the hour.");

    private async Task<(List<RecordedDecision> Decisions, List<DecisionRecordFileProblem> Problems)> ReadAsync(byte[] bytes)
    {
        var path = PathOf();
        await File.WriteAllBytesAsync(path, bytes, CancellationToken.None);
        var problems = new List<DecisionRecordFileProblem>();
        var decisions = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, CancellationToken.None))
        {
            decisions.Add(decision);
        }

        return (decisions, problems);
    }

    private static string Json(Action<Utf8JsonWriter> write) => Encoding.UTF8.GetString(Lines(write)).TrimEnd('\n');

    [Fact]
    public async Task ARecord_RoundTrips_ThroughTheFile()
    {
        var jsonState = DecisionContent.FromJson(JsonDocument.Parse("""{"subject":"Card","lines":["one","two"]}""").RootElement);
        var written = new[]
        {
            Record("d-1"),
            Record("d-2", erasureKey: null, state: jsonState, answers: [], errorKind: DecisionErrorKind.Refused, refusedKeys: ["urgent"]),
        };

        var (decisions, problems) = await ReadAsync(Lines(written.Select(Rec).ToArray()));

        Assert.Empty(problems);
        Assert.Equal(2, decisions.Count);
        for (var i = 0; i < written.Length; i++)
        {
            AssertSame(written[i], decisions[i].Record);
            Assert.Empty(decisions[i].Outcomes);
        }
    }

    private static void AssertSame(DecisionRecord expected, DecisionRecord actual)
    {
        Assert.Equal(expected.DecisionId, actual.DecisionId);
        Assert.Equal(expected.ErasureKey, actual.ErasureKey);
        Assert.Equal(expected.Started, actual.Started);
        Assert.Equal(expected.Started.Offset, actual.Started.Offset);
        Assert.Equal(expected.Latency, actual.Latency);
        Assert.Equal(expected.Provider, actual.Provider);
        Assert.Equal(expected.Endpoint, actual.Endpoint);
        Assert.Equal(expected.DefaultModel, actual.DefaultModel);
        Assert.Equal(expected.RequestedModel, actual.RequestedModel);
        Assert.Equal(expected.ServedModel, actual.ServedModel);
        Assert.Equal(expected.ResponseId, actual.ResponseId);
        Assert.Equal(expected.UpstreamProvider, actual.UpstreamProvider);
        Assert.Equal(expected.Usage, actual.Usage);
        Assert.Equal(expected.IsCached, actual.IsCached);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.ImageCount, actual.ImageCount);
        Assert.Equal(expected.ImageHashes, actual.ImageHashes);
        Assert.Equal(expected.ErrorKind, actual.ErrorKind);
        Assert.Equal(expected.RefusedKeys, actual.RefusedKeys);

        Assert.Equal(expected.Answers.Count, actual.Answers.Count);
        for (var i = 0; i < expected.Answers.Count; i++)
        {
            var (e, a) = (expected.Answers[i], actual.Answers[i]);
            Assert.Equal(e.Key, a.Key);
            Assert.Equal(e.Kind, a.Kind);
            Assert.Equal(e.ChosenIndex, a.ChosenIndex);
            Assert.Equal(e.Probability, a.Probability);
            Assert.Equal(e.Expected, a.Expected);
            Assert.Equal(e.Confidence, a.Confidence);
            Assert.Equal(e.Probabilities, a.Probabilities);
            Assert.Equal(e.ProbabilitySource, a.ProbabilitySource);
        }

        // The definition has no equality of its own: the same set writes the same JSON.
        Assert.Equal(
            Json(w => QuestionSetFormat.Write(w, expected.Definition)),
            Json(w => QuestionSetFormat.Write(w, actual.Definition)));
        Assert.Equal(expected.Definition.Questions.Count, actual.Definition.Questions.Count);
        Assert.Equal(expected.Definition.Questions[1].Options[0].Criterion!.Examples, actual.Definition.Questions[1].Options[0].Criterion!.Examples);
        Assert.Equal(expected.Definition.Questions[0].WhenTrue, actual.Definition.Questions[0].WhenTrue);
    }

    [Fact]
    public void TheLineFormat_IsPinned()
    {
        var text = Encoding.UTF8.GetString(Lines(
            Rec(Record()),
            Rec(Record("d-2", erasureKey: null, state: DecisionContent.FromJson(JsonDocument.Parse("""{"subject":"Card"}""").RootElement), answers: [], errorKind: DecisionErrorKind.Refused, refusedKeys: ["urgent"])),
            Out("d-1", Confirmed()),
            Out("d-1", Signal()),
            Erase("customer-9", null),
            Erase(null, "d-2")));

        TextSnapshot.VerifyText(text, "jsonl");
    }

    [Fact]
    public async Task Outcomes_AttachToTheirRecord_InFileOrder()
    {
        var first = Signal();
        var second = Confirmed();
        var (decisions, problems) = await ReadAsync(Lines(
            Rec(Record("d-1")),
            Rec(Record("d-2", erasureKey: null)),
            Out("d-1", first),
            Out("d-2", Signal()),
            Out("d-1", second)));

        Assert.Empty(problems);
        Assert.Equal(["d-1", "d-2"], decisions.Select(d => d.Record.DecisionId));
        Assert.Equal(2, decisions[0].Outcomes.Count);
        var signal = Assert.IsType<SignalOutcome>(decisions[0].Outcomes[0]);
        Assert.Equal(("mailer", T0.AddHours(1), "complained", 1d, "Wrote back within the hour."), (signal.Source, signal.At, signal.Name, signal.Value, signal.Note));
        var confirmed = Assert.IsType<ConfirmedOutcome>(decisions[0].Outcomes[1]);
        Assert.Equal("reviewer-ui", confirmed.Source);
        Assert.Equal(T0.AddMinutes(5), confirmed.At);
        Assert.Equal(ConfirmedAnswer.ForNoul(true), confirmed.Answers["urgent"]);
        Assert.Equal(ConfirmedAnswer.ForChoice("card_arrival"), confirmed.Answers["topic"]);
        Assert.Equal(ConfirmedAnswer.ForScore(2), confirmed.Answers["mood"]);
        Assert.Single(decisions[1].Outcomes);
    }

    [Fact]
    public async Task AnErasureByKey_RemovesEveryRecordWithThatKey()
    {
        var (decisions, problems) = await ReadAsync(Lines(
            Rec(Record("d-1", erasureKey: "customer-7")),
            Rec(Record("d-2", erasureKey: "customer-8")),
            Rec(Record("d-3", erasureKey: "customer-7")),
            Out("d-1", Signal()),
            Erase("customer-7", null),
            Out("d-3", Signal())));

        Assert.Empty(problems);
        Assert.Equal(["d-2"], decisions.Select(d => d.Record.DecisionId));
    }

    [Fact]
    public async Task AnErasureById_RemovesThatRecord()
    {
        var (decisions, problems) = await ReadAsync(Lines(
            Rec(Record("d-1")),
            Rec(Record("d-2")),
            Erase(null, "d-1")));

        Assert.Empty(problems);
        Assert.Equal(["d-2"], decisions.Select(d => d.Record.DecisionId));
    }

    [Fact]
    public async Task AnOrphanOutcome_IsIgnored_AndReported()
    {
        var (decisions, problems) = await ReadAsync(Lines(
            Rec(Record("d-1")),
            Out("secret-id-9", Signal())));

        Assert.Empty(Assert.Single(decisions).Outcomes);
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
        Assert.DoesNotContain("secret-id-9", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATruncatedLastLine_IsReported_NotThrown()
    {
        var bytes = Lines(Rec(Record("d-1")), Rec(Record("d-2")), Rec(Record("d-3")));
        var cut = bytes[..^40];

        var (decisions, problems) = await ReadAsync(cut);

        Assert.Equal(["d-1", "d-2"], decisions.Select(d => d.Record.DecisionId));
        var problem = Assert.Single(problems);
        Assert.Equal(3, problem.Line);
    }

    private static IReadOnlyList<RecordedAnswer> VerbalizedAnswers() =>
    [
        new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25], ProbabilitySource.Verbalized),
    ];

    [Fact]
    public void MarkedAnswer_IsWrittenWithItsSource_AsVersion2()
    {
        var line = Json(Rec(Record(answers: VerbalizedAnswers())));

        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
        var answer = document.RootElement.GetProperty("answers")[0];
        Assert.Equal("verbalized", answer.GetProperty("probabilitySource").GetString());
    }

    [Fact]
    public void ALineWithoutMarkedAnswers_StaysVersion1_AndWritesNoSource()
    {
        var line = Json(Rec(Record()));

        Assert.Contains("\"version\":1", line, StringComparison.Ordinal);
        Assert.DoesNotContain("probabilitySource", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkedLine_RoundTrips()
    {
        var written = Record(answers: [.. Answers(), .. VerbalizedAnswers(), new RecordedAnswer("mood", QuestionKind.Score, 1, 0, 1.25, 0.625, [0.25, 0.5, 0.25], ProbabilitySource.Logprobs)]);

        var (decisions, problems) = await ReadAsync(Lines(Rec(written)));

        Assert.Empty(problems);
        var answers = Assert.Single(decisions).Record.Answers;
        Assert.Equal(ProbabilitySource.Model, answers[0].ProbabilitySource);
        Assert.Equal(ProbabilitySource.Verbalized, answers[3].ProbabilitySource);
        Assert.Equal(ProbabilitySource.Logprobs, answers[4].ProbabilitySource);
        AssertSame(written, decisions[0].Record);
    }

    [Fact]
    public async Task Version1Line_ReadsAsModel()
    {
        var (decisions, problems) = await ReadAsync(Lines(Rec(Record())));

        Assert.Empty(problems);
        Assert.All(Assert.Single(decisions).Record.Answers, a => Assert.Equal(ProbabilitySource.Model, a.ProbabilitySource));
    }

    [Theory]
    [InlineData("guessed")]
    [InlineData("model")]
    public async Task UnknownSource_IsRejected(string source)
    {
        // "model" too: a model-made answer has no source in the file, so a written one is not a known value.
        var line = Json(Rec(Record(answers: VerbalizedAnswers()))).Replace("\"verbalized\"", "\"" + source + "\"", StringComparison.Ordinal);

        var (decisions, problems) = await ReadAsync(Encoding.UTF8.GetBytes(line + "\n"));

        Assert.Empty(decisions);
        Assert.Equal(1, Assert.Single(problems).Line);
    }

    [Fact]
    public async Task AWrongVersion_IsAProblem()
    {
        var future = """{"kind":"record","version":3,"decisionId":"d-9"}""" + "\n";
        byte[] bytes = [.. Lines(Rec(Record("d-1"))), .. Encoding.UTF8.GetBytes(future)];

        var (decisions, problems) = await ReadAsync(bytes);

        Assert.Single(decisions);
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("version 3", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("d-9", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheQuestionsMember_LoadsInTheDatasetLoader()
    {
        var line = Json(Rec(Record(state: DecisionContent.FromJson(JsonDocument.Parse("""{"subject":"Card"}""").RootElement))));
        using var document = JsonDocument.Parse(line);
        var questions = document.RootElement.GetProperty("questions").GetRawText();
        var state = document.RootElement.GetProperty("state").GetRawText();
        var dataset = $$$"""{"id":"d-1","state":{{{state}}},"questions":{{{questions}}},"expected":{"urgent":true,"topic":"card_arrival","mood":1}}""";

        var result = DecisionDataset.Parse(dataset);

        Assert.True(result.IsSuccess, string.Join("; ", result.IsSuccess ? [] : result.Error.Select(p => p.Message)));
        Assert.Equal(3, result.Value[0].Definition.Questions.Count);
    }

    [Fact]
    public void AJsonCriterion_LoadsInTheDatasetLoader_AsCriterionJson()
    {
        var jsonCriterion = Criterion.Json(DecisionContent.FromJson(JsonDocument.Parse("""{"description":"lvl b","weight":2}""").RootElement));
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice("topic", "Which?", new OptionDefinition("a", jsonCriterion), new OptionDefinition("b", null)),
            QuestionDefinition.Score("mood", "How?", "calm", Criterion.Json(DecisionContent.FromJson(JsonDocument.Parse("""["angry","very"]""").RootElement))));
        var line = Json(Rec(Record(definition: definition, answers: [])));
        using var document = JsonDocument.Parse(line);
        var questions = document.RootElement.GetProperty("questions").GetRawText();
        var dataset = $$$"""{"id":"d-1","state":"s","questions":{{{questions}}},"expected":{"topic":"a","mood":1}}""";

        var result = DecisionDataset.Parse(dataset);

        Assert.True(result.IsSuccess, string.Join("; ", result.IsSuccess ? [] : result.Error.Select(p => p.Message)));
        var loaded = result.Value[0].Definition.Questions;
        Assert.Null(loaded[0].Options[0].Criterion!.Description);
        Assert.True(loaded[0].Options[0].Criterion!.JsonContent!.Value.TryGetJson(out var json));
        Assert.Equal("""{"description":"lvl b","weight":2}""", json.GetRawText());
        Assert.Null(loaded[1].Options[1].Criterion!.Description);
    }

    [Fact]
    public async Task AJsonCriterion_ReadsBackAsJson_EvenWhenItLooksLikeATextCriterion()
    {
        var jsonCriterion = Criterion.Json(DecisionContent.FromJson(JsonDocument.Parse("""{"description":"lvl b","weight":2}""").RootElement));
        var definition = new QuestionSetDefinition(QuestionDefinition.Score("mood", "How?", "calm", jsonCriterion));

        var (decisions, problems) = await ReadAsync(Lines(Rec(Record(definition: definition, answers: []))));

        Assert.Empty(problems);
        var level = Assert.Single(decisions).Record.Definition.Questions[0].Options[1].Criterion!;
        Assert.Null(level.Description);
        Assert.True(level.JsonContent!.Value.TryGetJson(out var json));
        Assert.Equal("""{"description":"lvl b","weight":2}""", json.GetRawText());
    }

    private static ShadowResult Shadow(
        IReadOnlyList<RecordedAnswer>? answers = null, DecisionErrorKind? errorKind = null, DecisionUsage? usage = null)
        => new(
            "anthropic",
            "https://candidate.example.test/v1/",
            "candidate-default",
            "candidate-requested",
            "candidate-served-2026-10",
            "resp-shadow-1",
            "upstream-b",
            TimeSpan.FromTicks(65_122_500),
            usage,
            answers ?? [],
            errorKind,
            []);

    [Fact]
    public async Task ShadowedRecord_RoundTrips()
    {
        var shadow = Shadow(
            [
                new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.5, 0, 0.5, []),
                new RecordedAnswer("topic", QuestionKind.Choice, 1, 0, 0, 0.5, [0.25, 0.5, 0.25]),
            ],
            usage: new DecisionUsage { InputTokens = 90, OutputTokens = 11, Cost = 0.0005 });
        var record = Record().WithShadow(shadow);
        Assert.Contains("\"latencyMs\":6512.25", Json(Rec(record)), StringComparison.Ordinal);

        var (decisions, problems) = await ReadAsync(Lines(Rec(record)));

        Assert.Empty(problems);
        var read = Assert.Single(decisions).Record.Shadow!;
        Assert.Equal(shadow.Provider, read.Provider);
        Assert.Equal(shadow.Endpoint, read.Endpoint);
        Assert.Equal(shadow.DefaultModel, read.DefaultModel);
        Assert.Equal(shadow.RequestedModel, read.RequestedModel);
        Assert.Equal(shadow.ServedModel, read.ServedModel);
        Assert.Equal(shadow.ResponseId, read.ResponseId);
        Assert.Equal(shadow.UpstreamProvider, read.UpstreamProvider);
        Assert.Equal(shadow.Latency, read.Latency);
        Assert.Equal(shadow.Usage!.InputTokens, read.Usage!.InputTokens);
        Assert.Equal(shadow.Usage.OutputTokens, read.Usage.OutputTokens);
        Assert.Equal(shadow.Usage.Cost, read.Usage.Cost);
        Assert.Equal(shadow.Answers, read.Answers);
        Assert.Equal(shadow.ErrorKind, read.ErrorKind);
        Assert.Equal(shadow.RefusedKeys, read.RefusedKeys);
    }

    [Fact]
    public async Task ShadowedRecord_WithErrorKind_RoundTrips()
    {
        var shadow = Shadow(errorKind: DecisionErrorKind.Timeout);

        var (decisions, problems) = await ReadAsync(Lines(Rec(Record().WithShadow(shadow))));

        Assert.Empty(problems);
        var read = Assert.Single(decisions).Record.Shadow!;
        Assert.Equal(DecisionErrorKind.Timeout, read.ErrorKind);
        Assert.Empty(read.Answers);
        Assert.Empty(read.RefusedKeys);
        Assert.Null(read.Usage);
        Assert.Equal(shadow.Latency, read.Latency);
    }

    [Fact]
    public void CandidateVerbalizedAnswer_MakesTheLineVersion2()
    {
        var shadow = Shadow([new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.5, 0, 0.5, [], ProbabilitySource.Verbalized)]);

        var line = Json(Rec(Record().WithShadow(shadow)));

        Assert.Contains("\"version\":2", line, StringComparison.Ordinal);
        Assert.Contains("\"version\":1", Json(Rec(Record().WithShadow(Shadow()))), StringComparison.Ordinal);
    }

    [Fact]
    public void UnshadowedRecord_LineHasNoShadowMember()
        => Assert.DoesNotContain("\"shadow\"", Json(Rec(Record())), StringComparison.Ordinal);

    [Fact]
    public async Task ShadowWithUnknownErrorKind_IsAProblem()
    {
        var line = Json(Rec(Record("d-2").WithShadow(Shadow(errorKind: DecisionErrorKind.Timeout))))
            .Replace("\"errorKind\":\"Timeout\"", "\"errorKind\":\"Nope\"", StringComparison.Ordinal);
        Assert.Contains("Nope", line, StringComparison.Ordinal);

        var (decisions, problems) = await ReadAsync(Text(RecordText("d-1"), line));

        Assert.Equal(["d-1"], decisions.Select(d => d.Record.DecisionId));
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("does not know", problem.Message, StringComparison.Ordinal);
    }

    private static byte[] Text(params string[] lines) => Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n")));

    private static string RecordText(string id = "d-1") => Json(Rec(Record(id)));

    [Theory]
    [InlineData("errorKind")]
    [InlineData("answerKind")]
    public async Task ARecordWithAnErrorKindOrAnAnswerKindThisReaderDoesNotKnow_IsReported_AndSkipped(string what)
    {
        var record = RecordText("d-2");
        var unknown = string.Equals(what, "errorKind", StringComparison.Ordinal)
            ? record[..^1] + ""","errorKind":"Overheated"}"""
            : record.Replace("\"kind\":\"noul\"", "\"kind\":\"ranking\"", StringComparison.Ordinal);
        Assert.NotEqual(record, unknown);

        var (decisions, problems) = await ReadAsync(Text(RecordText("d-1"), unknown, Json(Erase("nobody", null))));

        Assert.Equal(["d-1"], decisions.Select(d => d.Record.DecisionId));
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("does not know", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVersionThatIsNotAnInteger_IsAProblem()
    {
        var (decisions, problems) = await ReadAsync(Text(RecordText(), """{"kind":"record","version":"two"}""", """{"kind":"record","version":1.5}"""));

        Assert.Single(decisions);
        Assert.Equal([2, 3], problems.Select(p => p.Line));
    }

    [Theory]
    [InlineData("""{"kind":"record","version":3,"decisionId":"d-9"}""")]
    [InlineData("""{"kind":"record","version":0,"decisionId":"d-9"}""")]
    [InlineData("""{"kind":"annotation","version":1,"decisionId":"d-9","text":"x"}""")]
    [InlineData("""{"kind":"outcome","version":1,"decisionId":"d-1","type":"rating","source":"ui","at":"2026-10-22T10:00:00+02:00","stars":4}""")]
    public async Task ALineOfAnUnknownVersionKindOrOutcomeType_IsAProblem_AndIsSkipped(string line)
    {
        var (decisions, problems) = await ReadAsync(Text(RecordText("d-1"), line, RecordText("d-2")));

        Assert.Equal(["d-1", "d-2"], decisions.Select(d => d.Record.DecisionId));
        Assert.All(decisions, d => Assert.Empty(d.Outcomes));
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
        foreach (var quoted in (string[])["d-9", "annotation", "rating"])
        {
            Assert.DoesNotContain(quoted, problem.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ALoneSurrogateEscape_InAnOutcomeKey_IsAProblem_NotAThrow()
    {
        var key = "\\ud800";
        var outcome = """{"kind":"outcome","version":1,"decisionId":"d-1","source":"ui","at":"2026-10-22T10:00:00+02:00","type":"confirmed","confirmed":{"KEY":true}}"""
            .Replace("KEY", key, StringComparison.Ordinal);

        var (decisions, problems) = await ReadAsync(Text(RecordText(), outcome));

        Assert.Empty(Assert.Single(decisions).Outcomes);
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
    }

    [Fact]
    public async Task ALoneSurrogateEscape_InAChoiceValue_IsAProblem_NotAThrow()
    {
        var outcome = """{"kind":"outcome","version":1,"decisionId":"d-1","source":"ui","at":"2026-10-22T10:00:00+02:00","type":"confirmed","confirmed":{"topic":"\ud800"}}""";

        var (decisions, problems) = await ReadAsync(Text(RecordText(), outcome));

        Assert.Empty(Assert.Single(decisions).Outcomes);
        Assert.Equal(2, Assert.Single(problems).Line);
    }

    [Fact]
    public async Task ABomOnTheFirstLine_AndCrlfLineEndings_AreRead()
    {
        var text = string.Join("\r\n", (string[])[RecordText("d-1"), Json(Out("d-1", Signal())), RecordText("d-2"), string.Empty]);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)];

        var (decisions, problems) = await ReadAsync(bytes);

        Assert.Empty(problems);
        Assert.Equal(["d-1", "d-2"], decisions.Select(d => d.Record.DecisionId));
        Assert.Single(decisions[0].Outcomes);
    }

    [Fact]
    public async Task ASyntaxProblem_DoesNotQuoteTheLine()
    {
        var broken = """{"kind":"record","version":1,"state":"SECRET-STATE-TEXT" oops,"decisionId":"secret-id-1"}""";

        var (decisions, problems) = await ReadAsync(Text(RecordText(), broken));

        Assert.Single(decisions);
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
        Assert.DoesNotContain("SECRET", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("oops", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnErasure_AppliesOnlyToEarlierLines()
    {
        var (decisions, problems) = await ReadAsync(Lines(
            Rec(Record("d-1", erasureKey: "customer-7")),
            Erase("customer-7", null),
            Rec(Record("d-2", erasureKey: "customer-7")),
            Rec(Record("d-1", erasureKey: "customer-7"))));

        Assert.Empty(problems);
        Assert.Equal(["d-2", "d-1"], decisions.Select(d => d.Record.DecisionId));
    }

    [Fact]
    public async Task ALaterRecordOfADecision_ReplacesTheEarlierOne_AndKeepsItsPlaceAndOutcomes()
    {
        var (decisions, problems) = await ReadAsync(Lines(
            Rec(Record("d-1", answers: [])),
            Rec(Record("d-2")),
            Out("d-1", Signal()),
            Rec(Record("d-1")),
            Out("d-1", Confirmed())));

        Assert.Empty(problems);
        Assert.Equal(["d-1", "d-2"], decisions.Select(d => d.Record.DecisionId));
        Assert.Equal(3, decisions[0].Record.Answers.Count);
        Assert.Equal(2, decisions[0].Outcomes.Count);
    }

    [Fact]
    public void ReadAsync_ChecksThePathBeforeTheFirstMoveNext()
    {
        Assert.Throws<ArgumentException>(() => DecisionRecordFile.ReadAsync(string.Empty, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.ReadAsync(null!, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => DecisionRecordFile.ReadAsync(string.Empty, new List<DecisionRecordFileProblem>(), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.ReadAsync("x", null!, CancellationToken.None));
    }

    [Fact]
    public void RecordedAnswers_CompareTheirProbabilitiesByValue()
    {
        var a = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25]);
        var b = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25]);
        var c = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.5, 0.5]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public async Task ATextState_IsAJsonString_AndAJsonStateIsRawJson()
    {
        var path = PathOf();
        await File.WriteAllBytesAsync(
            path,
            Lines(Rec(Record("d-1", state: "plain text")), Rec(Record("d-2", state: DecisionContent.FromJson(JsonDocument.Parse("""[1,2]""").RootElement)))),
            CancellationToken.None);

        var lines = (await File.ReadAllTextAsync(path, CancellationToken.None)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(JsonValueKind.String, JsonDocument.Parse(lines[0]).RootElement.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(lines[1]).RootElement.GetProperty("state").ValueKind);
    }
}
