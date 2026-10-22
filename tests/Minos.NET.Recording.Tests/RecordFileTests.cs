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
        int attempt = 0,
        DecisionContent? state = null,
        IReadOnlyList<RecordedAnswer>? answers = null,
        DecisionErrorKind? errorKind = null,
        IReadOnlyList<string>? refusedKeys = null)
        => new(
            id,
            erasureKey,
            attempt,
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
            Definition(),
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
            RecordLine.AppendLine(buffer, write);
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
        Assert.Equal(expected.Attempt, actual.Attempt);
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
    public async Task TheHighestAttempt_IsKept()
    {
        var (decisions, problems) = await ReadAsync(Lines(
            Rec(Record("d-1", attempt: 0, answers: [])),
            Out("d-1", Signal()),
            Rec(Record("d-1", attempt: 1)),
            Rec(Record("d-1", attempt: 0, answers: []))));

        Assert.Empty(problems);
        var decision = Assert.Single(decisions);
        Assert.Equal(1, decision.Record.Attempt);
        Assert.Equal(3, decision.Record.Answers.Count);
        Assert.Single(decision.Outcomes);
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

    [Fact]
    public async Task AWrongVersion_IsAProblem()
    {
        var future = """{"kind":"record","version":2,"decisionId":"d-9"}""" + "\n";
        byte[] bytes = [.. Lines(Rec(Record("d-1"))), .. Encoding.UTF8.GetBytes(future)];

        var (decisions, problems) = await ReadAsync(bytes);

        Assert.Single(decisions);
        var problem = Assert.Single(problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("version 2", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("d-9", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compact_RewritesWithoutErasedRecords_AndKeepsOutcomes()
    {
        var path = PathOf();
        await File.WriteAllBytesAsync(
            path,
            Lines(
                Rec(Record("d-1", erasureKey: "customer-7")),
                Rec(Record("d-2", erasureKey: "customer-8")),
                Rec(Record("d-2", erasureKey: "customer-8", attempt: 1)),
                Out("d-2", Confirmed()),
                Out("d-2", Signal()),
                Erase("customer-7", null),
                Out("ghost", Signal())),
            CancellationToken.None);
        var before = await DecisionsAsync(path);

        var problems = await DecisionRecordFile.CompactAsync(path, CancellationToken.None);

        Assert.Single(problems);
        var text = await File.ReadAllTextAsync(path, CancellationToken.None);
        Assert.DoesNotContain("\"kind\":\"erasure\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("d-1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ghost", text, StringComparison.Ordinal);
        Assert.Equal(3, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.False(File.Exists(path + ".compact.tmp"));

        var after = await DecisionsAsync(path);
        var (b, a) = (Assert.Single(before), Assert.Single(after));
        AssertSame(b.Record, a.Record);
        Assert.Equal(1, a.Record.Attempt);
        Assert.Equal(b.Outcomes.Count, a.Outcomes.Count);
        Assert.Equal(2, a.Outcomes.Count);
    }

    [Fact]
    public async Task Compact_LeavesAFileWithAnUnknownVersion_AsItIs()
    {
        var path = PathOf();
        var bytes = Lines(Rec(Record("d-1")), Erase("customer-7", null))
            .Concat(Encoding.UTF8.GetBytes("""{"kind":"record","version":2}""" + "\n"))
            .ToArray();
        await File.WriteAllBytesAsync(path, bytes, CancellationToken.None);

        var problems = await DecisionRecordFile.CompactAsync(path, CancellationToken.None);

        Assert.Single(problems);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, CancellationToken.None));
    }

    private static async Task<List<RecordedDecision>> DecisionsAsync(string path)
    {
        var list = new List<RecordedDecision>();
        await foreach (var d in DecisionRecordFile.ReadAsync(path, CancellationToken.None))
        {
            list.Add(d);
        }

        return list;
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
