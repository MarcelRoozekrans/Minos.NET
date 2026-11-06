using Minos.Evaluation;
using Minos.Samples.TicketTriage;
using ZeroAlloc.Results;

namespace Minos.Samples.Tests;

public sealed class TriageRunTests : IDisposable
{
    private const string Questions = """
        "questions":{"intent":{"type":"choice","instructions":"Which topic is this customer message about?","criteria":{"card_arrival":{"description":"Where a card is."},"lost_or_stolen_card":{"description":"A lost card."},"change_pin":{"description":"A new PIN."}}},"card_not_arrived":{"type":"noul","instructions":"Is the message about the arrival of a card?","criteria":{"true":"It is.","false":"It is not."}},"about_a_payment":{"type":"noul","instructions":"Is the message about a card payment?","criteria":{"true":"It is.","false":"It is not."}}}
        """;

    private readonly string _path = Path.Combine(Path.GetTempPath(), "triage-run-" + Guid.NewGuid().ToString("N") + ".jsonl");
    private readonly StringWriter _log = new();

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        _log.Dispose();
    }

    private static DecisionDataset Dataset(params string[] states)
    {
        var lines = states.Select((state, i) =>
            $$"""{"id":"t-{{i + 1}}","state":"{{state}}",{{Questions}},"expected":{"intent":"card_arrival","card_not_arrived":true,"about_a_payment":false},"source":"x#{{i + 1}}","version":1}""");
        var parsed = DecisionDataset.Parse(string.Join("\n", lines));
        Assert.True(parsed.IsSuccess);
        return parsed.Value;
    }

    // A clock the scripted clients move on by hand; 1000 ticks per second.
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ticks;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ticks);

        public void AdvanceMs(long ms) => _ticks += ms;
    }

    private sealed class Scripted(Func<DecisionRequest, Result<DecisionResponse, DecisionError>> answer) : IDecisionClient
    {
        public int Calls { get; private set; }

        public bool Disposed { get; private set; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new(answer(request));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Disposed = true;
    }

    private static Result<DecisionResponse, DecisionError> Ok(DecisionRequest request, int intent, double confidence, double noul)
    {
        var answers = request.Definition.Questions.Select(q => q.Kind == QuestionKind.Noul
            ? QuestionAnswer.Noul(noul)
            : QuestionAnswer.Choice(intent, confidence, Probabilities(q.Options.Count, intent, confidence))).ToArray();
        return Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, answers, "m"));
    }

    private static double[] Probabilities(int count, int chosen, double confidence)
    {
        var p = new double[count];
        for (var i = 0; i < count; i++)
        {
            p[i] = i == chosen ? confidence : (1 - confidence) / (count - 1);
        }

        return p;
    }

    private static Scripted Local(int intent = 0, double confidence = 0.95, double noul = 0.02, ManualTime? time = null, long takesMs = 0)
        => new(r =>
        {
            time?.AdvanceMs(takesMs);
            return Ok(r, intent, confidence, noul);
        });

    // Answers the definition it is asked, which is a subset of the questions when escalation re-asks.
    private static Scripted Target(int intent = 1, double confidence = 0.99, double noul = 0.01)
        => new(r => Ok(r, intent, confidence, noul));

    private static Scripted FailingTarget()
        => new(_ => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed")));

    private static async Task<TriageSummary> Run(Scripted local, Scripted target, DecisionDataset dataset, TriageRunTests t, TimeProvider? time = null, CancellationToken ct = default)
        => await TriageRun.RunAsync(local, target, dataset, t._path, t._log, time ?? TimeProvider.System, ct);

    [Fact]
    public async Task Pipeline_WithNoUnsureQuestion_ReturnsAPathOrNone()
    {
        var dataset = Dataset("a message");
        var item = dataset[0];
        var target = Target();
        var localSlot = new CallSlot();
        var targetSlot = new CallSlot();
        using var client = TriagePipeline.Build(Local(), target, localSlot, targetSlot);

        var result = await client.EvaluateAsync(new DecisionRequest(item.Definition, item.State));

        // Finding: the stage always attaches a path, so Path is never null; every route is Confident and answered by the
        // primary, the target has no certainty, and the only call is the primary's.
        Assert.True(result.IsSuccess);
        Assert.Equal(0, target.Calls);
        var path = Assert.IsType<DecisionPath>(result.Value.Path);
        Assert.Equal(3, path.Routes.Count);
        Assert.All(path.Routes, r =>
        {
            Assert.Equal(RouteReason.Confident, r.Reason);
            Assert.Equal(TriagePipeline.PrimaryName, r.AnsweredBy);
            Assert.Null(r.TargetCertainty);
        });
        var call = Assert.Single(path.Calls);
        Assert.Equal(TriagePipeline.PrimaryName, call.ClientName);
    }

    [Fact]
    public async Task Run_WritesOneLinePerItem_InDatasetOrder()
    {
        var local = Local();
        var target = Target();

        var summary = await Run(local, target, Dataset("one", "two", "three"), this);

        var records = TriageRecordReader.Read(_path);
        Assert.Equal(["t-1", "t-2", "t-3"], records.Select(r => r.Id));
        Assert.Equal(new TriageSummary(3, 3, 0, 0, 0, 0), summary);
        Assert.All(records, r => Assert.Equal(3, r.Questions.Count));
        Assert.True(local.Disposed);
        Assert.False(target.Disposed);
    }

    [Fact]
    public async Task Run_KeepsTheLocalAnswer_NextToTheTargetAnswer_ForAnEscalatedQuestion()
    {
        // Intent confidence 0.6 is below 0.85; the Noul answers are sure.
        var summary = await Run(Local(intent: 0, confidence: 0.6), Target(intent: 1, confidence: 0.97), Dataset("m"), this);

        var record = Assert.Single(TriageRecordReader.Read(_path));
        var intent = record.Questions.Single(q => string.Equals(q.Key, "intent", StringComparison.Ordinal));
        Assert.Equal("card_arrival", intent.Local.ChoiceKey);
        Assert.Equal("lost_or_stolen_card", intent.Final.ChoiceKey);
        Assert.Equal("Escalated", intent.Reason);
        Assert.Equal(TriagePipeline.TargetName, intent.AnsweredBy);
        Assert.Equal(0.6, intent.PrimaryCertainty, 6);
        Assert.NotNull(intent.TargetCertainty);
        var confident = record.Questions.Single(q => string.Equals(q.Key, "about_a_payment", StringComparison.Ordinal));
        Assert.Equal("Confident", confident.Reason);
        Assert.Null(confident.TargetCertainty);
        Assert.Equal(1, summary.EscalatedQuestions);
    }

    [Fact]
    public async Task Run_RecordsNoTimeLeft_WhenTheLocalCallUsesTheBudget()
    {
        var time = new ManualTime();
        var target = Target();

        var summary = await Run(Local(confidence: 0.6, time: time, takesMs: 4900), target, Dataset("m"), this, time);

        Assert.Equal(0, target.Calls);
        var record = Assert.Single(TriageRecordReader.Read(_path));
        var intent = record.Questions.Single(q => string.Equals(q.Key, "intent", StringComparison.Ordinal));
        Assert.Equal("NoTimeLeft", intent.Reason);
        Assert.Equal(intent.Local, intent.Final);
        var skipped = record.Calls.Single(c => string.Equals(c.Client, TriagePipeline.TargetName, StringComparison.Ordinal));
        Assert.Equal("NotCalledNoTimeLeft", skipped.Status);
        Assert.Null(skipped.LatencyMs);
        Assert.Equal(1, summary.NoTimeLeft);
    }

    [Fact]
    public async Task Run_RecordsTargetFailed_AndKeepsTheLocalAnswerAsFinal()
    {
        var summary = await Run(Local(confidence: 0.6), FailingTarget(), Dataset("m"), this);

        var record = Assert.Single(TriageRecordReader.Read(_path));
        var intent = record.Questions.Single(q => string.Equals(q.Key, "intent", StringComparison.Ordinal));
        Assert.Equal("TargetFailed", intent.Reason);
        Assert.Equal("card_arrival", intent.Final.ChoiceKey);
        Assert.Equal(intent.Local, intent.Final);
        Assert.Equal("Failed", record.Calls.Single(c => string.Equals(c.Client, TriagePipeline.TargetName, StringComparison.Ordinal)).Status);
        Assert.Equal(1, summary.TargetFailed);
        Assert.Equal(1, summary.Decided);
    }

    [Fact]
    public async Task Run_WritesAFailedLine_WhenTheLocalCallFails_AndContinues()
    {
        var calls = 0;
        var local = new Scripted(r => ++calls == 1
            ? Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed"))
            : Ok(r, 0, 0.95, 0.02));

        var summary = await Run(local, Target(), Dataset("one", "two"), this);

        var records = TriageRecordReader.Read(_path);
        Assert.Equal(2, records.Count);
        Assert.Equal("Server", records[0].Error);
        Assert.Empty(records[0].Questions);
        Assert.Empty(records[0].Calls);
        Assert.Null(records[1].Error);
        Assert.Contains("t-1: failed (Server)", _log.ToString(), StringComparison.Ordinal);
        Assert.Equal(new TriageSummary(2, 1, 1, 0, 0, 0), summary);
    }

    [Fact]
    public async Task Run_RecordsLatencyPerCall_FromThePath()
    {
        var time = new ManualTime();
        var local = Local(confidence: 0.6, time: time, takesMs: 120);
        var target = new Scripted(r =>
        {
            time.AdvanceMs(250);
            return Ok(r, 1, 0.97, 0.01);
        });

        await Run(local, target, Dataset("m"), this, time);

        var record = Assert.Single(TriageRecordReader.Read(_path));
        Assert.Equal(120, record.Calls.Single(c => string.Equals(c.Client, TriagePipeline.PrimaryName, StringComparison.Ordinal)).LatencyMs);
        Assert.Equal(250, record.Calls.Single(c => string.Equals(c.Client, TriagePipeline.TargetName, StringComparison.Ordinal)).LatencyMs);
        Assert.Equal(370, record.TotalMs);
    }

    [Fact]
    public async Task Run_LogsNoMessageText()
    {
        var local = new Scripted(r => ++localCalls == 1
            ? Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "SECRET-MESSAGE"))
            : Ok(r, 0, 0.6, 0.5));
        await Run(local, Target(), Dataset("SECRET-MESSAGE", "SECRET-MESSAGE"), this);

        Assert.DoesNotContain("SECRET-MESSAGE", _log.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-MESSAGE", await File.ReadAllTextAsync(_path), StringComparison.Ordinal);
        Assert.NotEmpty(_log.ToString());
    }

    private int localCalls;

    [Fact]
    public async Task Run_LogsProgressEvery25Items()
    {
        await Run(Local(), Target(), Dataset([.. Enumerable.Repeat("m", 26)]), this);

        Assert.Contains("25/26: decided 25, failed 0", _log.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("26/26", _log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_StopsOnCancel_KeepingWrittenLines()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var local = new Scripted(r =>
        {
            if (++calls == 2)
            {
                cts.Cancel();
            }

            return Ok(r, 0, 0.95, 0.02);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(local, Target(), Dataset("one", "two", "three"), this, ct: cts.Token));

        Assert.Equal(["t-1", "t-2"], TriageRecordReader.Read(_path).Select(r => r.Id));
    }

    [Fact]
    public async Task Run_SummarisesCountsByReason()
    {
        // Item 1 is sure, item 2 is escalated and answered, item 3 meets a failing target.
        var seen = 0;
        var local = new Scripted(r => ++seen == 1 ? Ok(r, 0, 0.95, 0.02) : Ok(r, 0, 0.6, 0.02));
        var targetCalls = 0;
        var target = new Scripted(r => ++targetCalls == 1
            ? Ok(r, 1, 0.97, 0.01)
            : Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed")));

        var summary = await Run(local, target, Dataset("one", "two", "three"), this);

        Assert.Equal(new TriageSummary(3, 3, 0, 1, 0, 1), summary);
    }
}
