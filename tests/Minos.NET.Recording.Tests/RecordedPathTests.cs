using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Minos.Recording.RecordFormat;
using ZeroAlloc.Results;
using static Minos.Recording.Tests.Stage;
using static Minos.Recording.Tests.TestRecords;

namespace Minos.Recording.Tests;

/// <summary>The recorder keeps the escalation path of each answer, and writes record format version 3 when there is one.</summary>
[Collection(RecordingListeners.Name)]
public sealed class RecordedPathTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("minos-recording-path-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static DecisionPath Path() => new(
        [
            new QuestionRoute("urgent", "local", RouteReason.Confident, 0.95, null),
            new QuestionRoute("topic", "hosted", RouteReason.Escalated, 0.5, 0.875),
        ],
        [
            new DecisionCall("local", "local-2b", new DecisionUsage { InputTokens = 7, OutputTokens = 2 }, false),
            new DecisionCall("hosted", "hosted-large", new DecisionUsage { InputTokens = 40, OutputTokens = 3, Cost = 0.002 }, false),
        ]);

    private static DecisionResponse AnswerWithPath() => new(
        Definition(),
        [QuestionAnswer.Noul(0.95), QuestionAnswer.Choice(1, 0.875, [0.125, 0.875])],
        model: "local-2b",
        usage: new DecisionUsage { InputTokens = 7 })
    {
        Path = Path(),
    };

    private static DecisionRecord WithPath(DecisionRecord record)
    {
        var path = Path();
        var answers = new[]
        {
            new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.25, 0, 0.75, [], ProbabilitySource.Model, "local", RouteReason.Confident, 0.75, null),
            new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25], ProbabilitySource.Model, "hosted", RouteReason.Escalated, 0.5, 0.75),
        };
        return new DecisionRecord(
            record.DecisionId, record.ErasureKey, record.Started, record.Latency, record.Provider, record.Endpoint, record.DefaultModel,
            record.RequestedModel, record.ServedModel, record.ResponseId, record.UpstreamProvider, record.Usage, record.IsCached,
            record.Definition, record.State, record.ImageCount, record.ImageHashes, answers, record.ErrorKind, record.RefusedKeys,
            calls: path.Calls);
    }

    private static byte[] Bytes(params DecisionRecord[] records)
    {
        var buffer = new ArrayBufferWriter<byte>();
        foreach (var record in records)
        {
            RecordLine.WriteTo(buffer, w => RecordLine.Write(w, record));
            buffer.Write("\n"u8);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private async Task<(List<RecordedDecision> Decisions, List<DecisionRecordFileProblem> Problems)> ReadAsync(byte[] bytes)
    {
        var file = System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".jsonl");
        await File.WriteAllBytesAsync(file, bytes, CancellationToken.None);
        var problems = new List<DecisionRecordFileProblem>();
        var decisions = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(file, problems, CancellationToken.None))
        {
            decisions.Add(decision);
        }

        return (decisions, problems);
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimEnd('\n');

    private sealed class Primary(DecisionResponse? shortcut = null) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(shortcut ?? Reply(request.Definition, 0.5, "local-2b")));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // Sure about "urgent", unsure about "topic"; answers only the questions the definition has.
    private static DecisionResponse Reply(QuestionSetDefinition definition, double topicConfidence, string model)
        => new(
            definition,
            [.. definition.Questions.Select(q => string.Equals(q.Key, "urgent", StringComparison.Ordinal) ? QuestionAnswer.Noul(0.95) : QuestionAnswer.Choice(1, topicConfidence, [1 - topicConfidence, topicConfidence]))],
            model,
            new DecisionUsage { InputTokens = 5 });

    private sealed class Target : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(Reply(request.Definition, 0.875, "hosted-large")));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task A_recorded_escalation_keeps_routes_and_calls()
    {
        var sink = new InMemoryDecisionRecordSink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var services = new ServiceCollection().AddSingleton(recorder).BuildServiceProvider();
            using var client = new Primary().AsBuilder()
                .UseRecording()
                .UseEscalation(new Target(), o => o.TargetName = "hosted")
                .Build(services);

            var result = await client.EvaluateAsync(Request());
            Assert.True(result.IsSuccess);
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
        }

        var record = Assert.Single(sink.Records).Record;
        Assert.NotNull(record.Calls);
        Assert.Equal(["primary", "hosted"], record.Calls.Select(c => c.ClientName));
        Assert.Equal("local-2b", record.Calls[0].Model);
        Assert.Equal("hosted-large", record.Calls[1].Model);
        var urgent = record.Answers[0];
        Assert.Equal("primary", urgent.AnsweredBy);
        Assert.Equal(RouteReason.Confident, urgent.Reason);
        Assert.Null(urgent.TargetCertainty);
        var topic = record.Answers[1];
        Assert.Equal("hosted", topic.AnsweredBy);
        Assert.Equal(RouteReason.Escalated, topic.Reason);
        Assert.Equal(0.5, topic.PrimaryCertainty);
        Assert.Equal(0.875, topic.TargetCertainty);
    }

    [Fact]
    public async Task The_JSONL_line_round_trips_version_3()
    {
        var written = WithPath(Record());

        var bytes = Bytes(written);
        var (decisions, problems) = await ReadAsync(bytes);

        Assert.Contains("\"version\":3", Text(bytes), StringComparison.Ordinal);
        Assert.Empty(problems);
        var read = Assert.Single(decisions).Record;
        Assert.Equal(written.Answers, read.Answers);
        Assert.Equal(written.Calls, read.Calls);
        Assert.Equal(Text(bytes), Text(Bytes(read)));
    }

    [Fact]
    public void The_path_line_format_is_pinned()
    {
        var line = Text(Bytes(WithPath(Record())));

        Assert.Contains(
            ""","answers":[{"key":"urgent","kind":"noul","probability":0.25,"confidence":0.75,"answeredBy":"local","reason":"Confident","primaryCertainty":0.75},{"key":"topic","kind":"choice","chosenIndex":0,"confidence":0.75,"answeredBy":"hosted","reason":"Escalated","primaryCertainty":0.5,"targetCertainty":0.75,"probabilities":[0.75,0.25]}],"calls":[{"client":"local","model":"local-2b","usage":{"inputTokens":7,"outputTokens":2},"cached":false},{"client":"hosted","model":"hosted-large","usage":{"inputTokens":40,"outputTokens":3,"cost":0.002},"cached":false}]""",
            line,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versions_1_and_2_still_read()
    {
        var marked = new DecisionRecord(
            "d-2", null, T0, TimeSpan.FromMilliseconds(5), "openai", null, null, null, null, null, null, null, false,
            Definition(), DecisionContent.FromString("x"), 0, [],
            [new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.5, 0, 0.5, [], ProbabilitySource.Verbalized)], null, []);
        var bytes = Bytes(Record(), marked);

        var (decisions, problems) = await ReadAsync(bytes);

        Assert.Empty(problems);
        Assert.Equal(2, decisions.Count);
        Assert.All(decisions, d => Assert.Null(d.Record.Calls));
        Assert.All(decisions.SelectMany(d => d.Record.Answers), a => Assert.Null(a.AnsweredBy));
        Assert.Equal(ProbabilitySource.Verbalized, decisions[1].Record.Answers[0].ProbabilitySource);
    }

    [Fact]
    public void A_record_without_a_path_writes_version_1_or_2_as_before()
    {
        var plain = Text(Bytes(Record()));
        var marked = Text(Bytes(new DecisionRecord(
            "d-2", null, T0, TimeSpan.Zero, "openai", null, null, null, null, null, null, null, false, Definition(),
            DecisionContent.FromString("x"), 0, [],
            [new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.5, 0, 0.5, [], ProbabilitySource.Logprobs)], null, [])));

        Assert.Contains("\"version\":1", plain, StringComparison.Ordinal);
        Assert.Contains("\"version\":2", marked, StringComparison.Ordinal);
        Assert.DoesNotContain("calls", plain + marked, StringComparison.Ordinal);
        Assert.DoesNotContain("answeredBy", plain + marked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_with_a_marked_answer_is_version_3_and_keeps_the_source()
    {
        var path = WithPath(Record());
        var marked = new DecisionRecord(
            path.DecisionId, null, T0, TimeSpan.Zero, "openai", null, null, null, null, null, null, null, false, path.Definition,
            path.State, 0, [],
            [
                new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.5, 0, 0.5, [], ProbabilitySource.Verbalized, "local", RouteReason.Confident, 0.5, null),
                path.Answers[1],
            ],
            null, [], calls: path.Calls);

        var bytes = Bytes(marked);
        var (decisions, problems) = await ReadAsync(bytes);

        Assert.Contains("\"version\":3", Text(bytes), StringComparison.Ordinal);
        Assert.Empty(problems);
        Assert.Equal(marked.Answers, Assert.Single(decisions).Record.Answers);
    }

    [Fact]
    public async Task A_shadow_write_keeps_the_primarys_path()
    {
        var sink = new RecordingSpySink();
        var (recorder, _, factory) = Recorder(sink);
        using (factory)
        await using (recorder)
        {
            var services = new ServiceCollection().AddSingleton(recorder).BuildServiceProvider();
            var candidate = new ShadowCandidate(held: false);
            using var client = new Primary(AnswerWithPath()).AsBuilder().UseRecording(new ShadowOptions(candidate, 10)).Build(services);

            Assert.True((await client.EvaluateAsync(Request())).IsSuccess);
            await candidate.WaitStartedAsync();
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            while (sink.Inner.Records[0].Record.Shadow is null)
            {
                Assert.True(deadline.Elapsed < Bound, "The decision was not shadowed in time.");
                await Task.Delay(5);
                await recorder.FlushAsync().AsTask().WaitAsync(Bound);
            }
        }

        var record = Assert.Single(sink.Inner.Records).Record;
        Assert.NotNull(record.Shadow);
        Assert.Equal(["local", "hosted"], record.Calls!.Select(c => c.ClientName));
        Assert.Equal("hosted", record.Answers[1].AnsweredBy);
        Assert.All(record.Shadow.Answers, a => Assert.Null(a.AnsweredBy));
    }

    [Theory]
    [InlineData("\"reason\":\"Escalated\"", "\"reason\":\"Bogus\"")]
    [InlineData("\"reason\":\"Escalated\"", "\"reason\":\"7\"")]
    [InlineData("\"primaryCertainty\":0.5", "\"primaryCertainty\":1.5")]
    [InlineData("\"targetCertainty\":0.75", "\"targetCertainty\":-0.25")]
    [InlineData("\"answeredBy\":\"hosted\",", "")]
    [InlineData("\"cached\":false}]", "}]")]
    [InlineData("\"client\":\"hosted\"", "\"client\":\"\"")]
    [InlineData("\"version\":3", "\"version\":2")]
    public async Task A_malformed_path_is_one_problem_and_the_other_lines_still_read(string find, string replace)
    {
        var bad = Text(Bytes(WithPath(Record("d-bad")))).Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(Text(Bytes(WithPath(Record("d-bad")))), bad);
        var good = Text(Bytes(Record("d-good")));

        var (decisions, problems) = await ReadAsync(Encoding.UTF8.GetBytes(bad + "\n" + good + "\n"));

        Assert.Equal("d-good", Assert.Single(decisions).Record.DecisionId);
        Assert.Equal(1, Assert.Single(problems).Line);
    }

    private async Task AssertOneProblemAsync(string badLine)
    {
        var good = Text(Bytes(Record("d-good")));

        var (decisions, problems) = await ReadAsync(Encoding.UTF8.GetBytes(badLine + "\n" + good + "\n"));

        Assert.Equal("d-good", Assert.Single(decisions).Record.DecisionId);
        Assert.Equal(1, Assert.Single(problems).Line);
    }

    [Fact]
    public async Task A_version_3_line_with_route_members_but_no_calls_is_a_problem()
    {
        var line = Text(Bytes(WithPath(Record("d-bad"))));
        var calls = line.IndexOf(",\"calls\":", StringComparison.Ordinal);

        await AssertOneProblemAsync(line[..calls] + "}");
    }

    [Fact]
    public async Task Empty_calls_are_a_problem()
    {
        var line = Text(Bytes(WithPath(Record("d-bad"))));
        var calls = line.IndexOf(",\"calls\":", StringComparison.Ordinal);

        await AssertOneProblemAsync(line[..calls] + ",\"calls\":[]}");
    }

    [Theory]
    [InlineData("\"inputTokens\":7", "\"inputTokens\":-7")]
    [InlineData("\"outputTokens\":2", "\"outputTokens\":-2")]
    [InlineData("\"cost\":0.002", "\"cost\":-0.002")]
    public async Task A_call_with_negative_usage_is_a_problem(string find, string replace)
        => await AssertOneProblemAsync(Text(Bytes(WithPath(Record("d-bad")))).Replace(find, replace, StringComparison.Ordinal));

    [Fact]
    public async Task A_shadow_answer_with_route_members_is_a_problem()
    {
        var line = Text(Bytes(Record("d-bad").WithShadow(Shadow())))
            .Replace("\"version\":1", "\"version\":3", StringComparison.Ordinal)
            .Replace("\"confidence\":0.5}]", "\"confidence\":0.5,\"answeredBy\":\"x\",\"reason\":\"Confident\",\"primaryCertainty\":0.5}]", StringComparison.Ordinal);
        Assert.Contains("answeredBy", line, StringComparison.Ordinal);

        await AssertOneProblemAsync(line);
    }

    [Fact]
    public void A_record_cannot_carry_calls_with_an_answer_that_has_no_route()
    {
        var withPath = WithPath(Record());
        var partial = new[] { withPath.Answers[0], Record().Answers[1] };

        Assert.Throws<ArgumentException>(() => new DecisionRecord(
            "d", null, T0, TimeSpan.Zero, "p", null, null, null, null, null, null, null, false, withPath.Definition, withPath.State, 0, [],
            partial, null, [], calls: withPath.Calls));
        Assert.Throws<ArgumentException>(() => new DecisionRecord(
            "d", null, T0, TimeSpan.Zero, "p", null, null, null, null, null, null, null, false, withPath.Definition, withPath.State, 0, [],
            withPath.Answers, null, [], calls: null));
    }

    [Fact]
    public async Task A_version_4_line_is_still_rejected()
    {
        var line = Text(Bytes(WithPath(Record()))).Replace("\"version\":3", "\"version\":4", StringComparison.Ordinal);

        var (decisions, problems) = await ReadAsync(Encoding.UTF8.GetBytes(line + "\n"));

        Assert.Empty(decisions);
        Assert.Contains("1 to 3", Assert.Single(problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_members_take_part_in_equality()
    {
        var a = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25], ProbabilitySource.Model, "hosted", RouteReason.Escalated, 0.5, 0.75);
        var same = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25], ProbabilitySource.Model, "hosted", RouteReason.Escalated, 0.5, 0.75);
        var other = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25], ProbabilitySource.Model, "local", RouteReason.Escalated, 0.5, 0.75);
        var noPath = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25]);

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, other);
        Assert.NotEqual(a, noPath);
    }
}
