using System.Buffers;
using Minos.Recording;

namespace Minos.AotSmoke;

/// <summary>
/// The decision recorder under Native AOT: a decision recorded through the JSONL sink, its outcomes, an erasure, a flush
/// and the file read back; the stage, the recorder and the sinks called through their interfaces; and the
/// recorder's values and exceptions.
/// </summary>
internal static class RecordingChecks
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    [Covers("Minos.Recording.JsonlDecisionRecordSink.JsonlDecisionRecordSink(string! path) -> void")]
    [Covers("Minos.Recording.RecordingOptions.RecordingOptions() -> void")]
    [Covers("Minos.Recording.DecisionRecorder.DecisionRecorder(Minos.Recording.IDecisionRecordSink! sink, Minos.Recording.RecordingOptions? options = null, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null, System.TimeProvider? timeProvider = null) -> void")]
    [Covers("static Minos.RecordingDecisionClientBuilderExtensions.UseRecording(this Minos.DecisionClientBuilder! builder, Minos.Recording.DecisionRecorder? recorder = null) -> Minos.DecisionClientBuilder!")]
    [Covers("static Minos.Recording.DecisionRecording.Begin(string? decisionId = null, string? erasureKey = null) -> Minos.Recording.DecisionRecordingScope!")]
    [Covers("Minos.Recording.DecisionRecordingScope.Dispose() -> void")]
    [Covers("static Minos.Recording.RedactionResult.Keep(Minos.DecisionContent state) -> Minos.Recording.RedactionResult!")]
    [Covers("static Minos.Recording.DecisionOutcome.Confirmed(string! source, System.DateTimeOffset at, System.Collections.Generic.IReadOnlyDictionary<string!, Minos.Recording.ConfirmedAnswer>! answers) -> Minos.Recording.DecisionOutcome!")]
    [Covers("static Minos.Recording.DecisionOutcome.Signal(string! source, System.DateTimeOffset at, string! name, double? value = null, string? note = null) -> Minos.Recording.DecisionOutcome!")]
    [Covers("static Minos.Recording.ConfirmedAnswer.ForChoice(string! optionKey) -> Minos.Recording.ConfirmedAnswer")]
    [Covers("Minos.Recording.DecisionRecorder.AddOutcomeAsync(string! decisionId, Minos.Recording.DecisionOutcome! outcome, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.DecisionRecorder.EraseDecisionAsync(string! decisionId, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.DecisionRecorder.FlushAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.DecisionRecorder.EraseAsync(string! erasureKey, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.DecisionRecorder.DisposeAsync() -> System.Threading.Tasks.ValueTask")]
    [Covers("static Minos.Recording.DecisionRecordFile.ReadAsync(string! path, System.Collections.Generic.ICollection<Minos.Recording.DecisionRecordFileProblem>! problems, System.Threading.CancellationToken cancellationToken) -> System.Collections.Generic.IAsyncEnumerable<Minos.Recording.RecordedDecision!>!")]
    [Covers("static Minos.Recording.DecisionRecordFile.ReadAsync(string! path, System.Threading.CancellationToken cancellationToken) -> System.Collections.Generic.IAsyncEnumerable<Minos.Recording.RecordedDecision!>!")]
    [Covers("Minos.Recording.JsonlDecisionRecordSink.WriteAsync(Minos.Recording.DecisionRecord! record, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.JsonlDecisionRecordSink.AddOutcomeAsync(string! decisionId, Minos.Recording.DecisionOutcome! outcome, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.JsonlDecisionRecordSink.EraseAsync(string! erasureKey, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.JsonlDecisionRecordSink.EraseDecisionAsync(string! decisionId, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.JsonlDecisionRecordSink.DisposeAsync() -> System.Threading.Tasks.ValueTask")]
    public static async Task RecordsADecisionToAFileAndReadsItBack()
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        try
        {
            var sink = new JsonlDecisionRecordSink(path);
            var options = new RecordingOptions { Capacity = 16, FlushTimeout = TimeSpan.FromSeconds(10), Redact = static state => RedactionResult.Keep(state) };
            var recorder = new DecisionRecorder(sink, options);
            using var client = new FixedResponseClient(PipelineChecks.TriageResponse()).AsBuilder().UseRecording(recorder).Build();

            var scope = DecisionRecording.Begin(erasureKey: "customer-7");
            var first = await client.EvaluateAsync(request).ConfigureAwait(false);
            var decisionId = scope.DecisionId;
            scope.Dispose();

            var second = DecisionRecording.Begin(decisionId: "smoke-2");
            _ = await client.EvaluateAsync(request).ConfigureAwait(false);
            second.Dispose();

            await recorder.AddOutcomeAsync(
                decisionId!,
                DecisionOutcome.Confirmed("reviewer", At, new Dictionary<string, ConfirmedAnswer>(StringComparer.Ordinal) { ["team"] = ConfirmedAnswer.ForChoice("account") })).ConfigureAwait(false);
            await recorder.AddOutcomeAsync(decisionId!, DecisionOutcome.Signal("mailer", At, "complained", 1, "Wrote back.")).ConfigureAwait(false);
            await recorder.EraseDecisionAsync("smoke-2").ConfigureAwait(false);
            await recorder.FlushAsync().ConfigureAwait(false);

            var problems = new List<DecisionRecordFileProblem>();
            var read = await ReadAllAsync(DecisionRecordFile.ReadAsync(path, problems, CancellationToken.None)).ConfigureAwait(false);

            Program.Check(
                first.IsSuccess
                    && Guid.TryParse(decisionId, out var guid) && guid.Version == 7
                    && problems.Count == 0
                    && read.Count == 1
                    && string.Equals(read[0].Record.DecisionId, decisionId, StringComparison.Ordinal)
                    && string.Equals(read[0].Record.ErasureKey, "customer-7", StringComparison.Ordinal)
                    && read[0].Record.Answers.Count == 3
                    && read[0].Outcomes is [ConfirmedOutcome { Answers.Count: 1 }, SignalOutcome { Name: "complained", Value: 1 }],
                "a decision is recorded through the JSONL sink with its outcomes, and an erased one is gone, under Native AOT");

            await recorder.EraseAsync("customer-7").ConfigureAwait(false);
            await recorder.DisposeAsync().ConfigureAwait(false);

            // The recorder is gone, so the app is the sink's one caller again.
            var record = read[0].Record;
            await sink.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);
            await sink.AddOutcomeAsync(record.DecisionId, DecisionOutcome.Signal("mailer", At, "opened"), CancellationToken.None).ConfigureAwait(false);
            await sink.EraseDecisionAsync(record.DecisionId, CancellationToken.None).ConfigureAwait(false);
            await sink.EraseAsync("customer-7", CancellationToken.None).ConfigureAwait(false);
            await sink.DisposeAsync().ConfigureAwait(false);

            var afterErasure = await ReadAllAsync(DecisionRecordFile.ReadAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Program.Check(
                afterErasure.Count == 0,
                "an erasure by key removes the customer's records, under Native AOT");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Covers("Minos.Recording.InMemoryDecisionRecordSink.InMemoryDecisionRecordSink() -> void")]
    [Covers("Minos.Recording.RecordingDecisionClient.RecordingDecisionClient(Minos.IDecisionClient! innerClient, Minos.Recording.DecisionRecorder! recorder) -> void")]
    [Covers("override Minos.Recording.RecordingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("override Minos.Recording.RecordingDecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    [Covers("Minos.Recording.IDecisionRecorder.AddOutcomeAsync(string! decisionId, Minos.Recording.DecisionOutcome! outcome, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.IDecisionRecorder.EraseAsync(string! erasureKey, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.IDecisionRecorder.EraseDecisionAsync(string! decisionId, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.IDecisionRecorder.FlushAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.IDecisionRecordSink.WriteAsync(Minos.Recording.DecisionRecord! record, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.IDecisionRecordSink.AddOutcomeAsync(string! decisionId, Minos.Recording.DecisionOutcome! outcome, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.IDecisionRecordSink.EraseAsync(string! erasureKey, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.IDecisionRecordSink.EraseDecisionAsync(string! decisionId, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.InMemoryDecisionRecordSink.WriteAsync(Minos.Recording.DecisionRecord! record, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.InMemoryDecisionRecordSink.AddOutcomeAsync(string! decisionId, Minos.Recording.DecisionOutcome! outcome, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.InMemoryDecisionRecordSink.EraseAsync(string! erasureKey, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    [Covers("Minos.Recording.InMemoryDecisionRecordSink.EraseDecisionAsync(string! decisionId, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask")]
    public static async Task TheStageRecorderAndSinksWorkThroughTheirInterfaces()
    {
        var memory = new InMemoryDecisionRecordSink();
        var recorder = new DecisionRecorder(memory);
        using var stage = new RecordingDecisionClient(new FixedResponseClient(PipelineChecks.TriageResponse()), recorder);

        var result = await stage.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);
        var found = stage.GetService(typeof(IDecisionRecorder));

        // The interface the stage hands out, as an app that keeps only the client reaches it.
        var (outcomes, afterErasure) = await ThroughTheRecorderInterfaceAsync(stage.GetService<IDecisionRecorder>()!, memory).ConfigureAwait(false);
        await recorder.DisposeAsync().ConfigureAwait(false);

        Program.Check(
            result.IsSuccess && ReferenceEquals(found, recorder) && outcomes == 1 && afterErasure == 0,
            "the recording stage records through an in-memory sink, and the recorder takes outcomes and erasures through its interface, under Native AOT");

        // The sink interface over both shipped sinks: the recorder's view of a sink.
        var record = await RecordOneAsync().ConfigureAwait(false);
        var viaInterface = new InMemoryDecisionRecordSink();
        await ThroughTheSinkInterfaceAsync(viaInterface, record).ConfigureAwait(false);
        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-" + Guid.NewGuid().ToString("N") + ".jsonl");
        List<RecordedDecision> fromFile;
        try
        {
            var file = new JsonlDecisionRecordSink(path);
            await ThroughTheSinkInterfaceAsync(file, record).ConfigureAwait(false);
            await file.DisposeAsync().ConfigureAwait(false);
            fromFile = await ReadAllAsync(DecisionRecordFile.ReadAsync(path, CancellationToken.None)).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }

        var direct = new InMemoryDecisionRecordSink();
        await direct.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);
        await direct.AddOutcomeAsync(record.DecisionId, DecisionOutcome.Signal("mailer", At, "opened"), CancellationToken.None).ConfigureAwait(false);
        var directOutcomes = direct.Records[0].Outcomes.Count;
        await direct.EraseAsync("nobody", CancellationToken.None).ConfigureAwait(false);
        await direct.EraseDecisionAsync(record.DecisionId, CancellationToken.None).ConfigureAwait(false);

        Program.Check(
            viaInterface.Records.Count == 0 && fromFile.Count == 0 && directOutcomes == 1 && direct.Records.Count == 0,
            "both sinks take records, outcomes, an orphan outcome and erasures through the sink interface, and the in-memory sink through its own type, under Native AOT");
    }

    [Covers("Minos.Recording.ConfirmedAnswer.ConfirmedAnswer() -> void")]
    [Covers("Minos.Recording.ConfirmedAnswer.Equals(Minos.Recording.ConfirmedAnswer other) -> bool")]
    [Covers("static Minos.Recording.ConfirmedAnswer.ForNoul(bool value) -> Minos.Recording.ConfirmedAnswer")]
    [Covers("static Minos.Recording.ConfirmedAnswer.ForScore(int level) -> Minos.Recording.ConfirmedAnswer")]
    [Covers("Minos.Recording.RecordedAnswer.RecordedAnswer() -> void")]
    [Covers("Minos.Recording.RecordedAnswer.Equals(Minos.Recording.RecordedAnswer other) -> bool")]
    [Covers("Minos.Recording.DecisionRecordFileProblem.DecisionRecordFileProblem() -> void")]
    [Covers("Minos.Recording.DecisionRecordFileProblem.DecisionRecordFileProblem(int Line, string! Message) -> void")]
    [Covers("Minos.Recording.RecordingException.RecordingException() -> void")]
    [Covers("Minos.Recording.RecordingException.RecordingException(string! message) -> void")]
    [Covers("Minos.Recording.RecordingException.RecordingException(string! message, System.Exception? innerException) -> void")]
    public static async Task ValuesProblemsAndExceptions()
    {
        var noul = ConfirmedAnswer.ForNoul(true);
        var score = ConfirmedAnswer.ForScore(2);
        // Only the library makes a recorded answer: take one from a record.
        var recorded = await RecordOneAsync().ConfigureAwait(false);
        var answer = recorded.Answers[1];
        var blankAnswer = new RecordedAnswer();
        var problem = new DecisionRecordFileProblem(3, "a message");
        var blankProblem = new DecisionRecordFileProblem();

        Program.Check(
            noul.Equals(ConfirmedAnswer.ForNoul(true))
                && !noul.Equals(score)
                && score.ScoreLevel == 2
                && new ConfirmedAnswer().Equals(default)
                && answer.Equals(recorded.Answers[1])
                && answer.Kind == QuestionKind.Choice && answer.Probabilities.Count > 0
                && !answer.Equals(blankAnswer)
                && blankAnswer.Key is null && blankAnswer.Probabilities.Count == 0
                && problem.Line == 3 && blankProblem.Line == 0,
            "confirmed answers, recorded answers and file problems are values under Native AOT");

        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-" + Guid.NewGuid().ToString("N") + ".jsonl");
        await File.WriteAllTextAsync(path, "{\"kind\":\"record\"\nnot json\n{\"kind\":\"record\",\"version\":2}\n").ConfigureAwait(false);
        var found = new List<DecisionRecordFileProblem>();
        List<RecordedDecision> none;
        try
        {
            none = await ReadAllAsync(DecisionRecordFile.ReadAsync(path, found, CancellationToken.None)).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }

        var lines = found.ConvertAll(problem => problem.Line);

        var inner = new IOException("disk full");
        Program.Check(
            none.Count == 0 && lines is [1, 2, 3]
                && new RecordingException().Message.Length > 0
                && string.Equals(new RecordingException("failed").Message, "failed", StringComparison.Ordinal)
                && ReferenceEquals(new RecordingException("failed", inner).InnerException, inner),
            "a bad record file line is a problem with its line number, and RecordingException has its constructors, under Native AOT");

        var scope = DecisionRecording.Begin();
        var nested = false;
        try
        {
            _ = DecisionRecording.Begin();
        }
        catch (InvalidOperationException)
        {
            nested = true;
        }
        finally
        {
            scope.Dispose();
        }

        Program.Check(nested, "a recording scope cannot nest under Native AOT");
    }

    [Covers("static Minos.Recording.DecisionRecordFile.WriteRecordLine(System.Buffers.IBufferWriter<byte>! destination, Minos.Recording.DecisionRecord! record) -> void")]
    [Covers("static Minos.Recording.DecisionRecordFile.WriteOutcomeLine(System.Buffers.IBufferWriter<byte>! destination, string! decisionId, Minos.Recording.DecisionOutcome! outcome) -> void")]
    [Covers("static Minos.Recording.DecisionRecordFile.WriteErasureLine(System.Buffers.IBufferWriter<byte>! destination, string? erasureKey, string? decisionId) -> void")]
    public static async Task TheLineWritersMakeAFileTheReaderReads()
    {
        // A sink of its own stores the lines and exports them, each ended with a line feed.
        var record = await RecordOneAsync().ConfigureAwait(false);
        var buffer = new ArrayBufferWriter<byte>();
        DecisionRecordFile.WriteRecordLine(buffer, record);
        buffer.Write("\n"u8);
        DecisionRecordFile.WriteOutcomeLine(buffer, record.DecisionId, DecisionOutcome.Signal("mailer", At, "opened"));
        buffer.Write("\n"u8);
        DecisionRecordFile.WriteRecordLine(buffer, record);
        buffer.Write("\n"u8);
        DecisionRecordFile.WriteErasureLine(buffer, "nobody", null);
        buffer.Write("\n"u8);
        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-" + Guid.NewGuid().ToString("N") + ".jsonl");
        List<RecordedDecision> kept;
        List<RecordedDecision> erased;
        try
        {
            await File.WriteAllBytesAsync(path, buffer.WrittenMemory.ToArray()).ConfigureAwait(false);
            kept = await ReadAllAsync(DecisionRecordFile.ReadAsync(path, CancellationToken.None)).ConfigureAwait(false);
            buffer.ResetWrittenCount();
            DecisionRecordFile.WriteErasureLine(buffer, null, record.DecisionId);
            buffer.Write("\n"u8);
            await File.AppendAllTextAsync(path, System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan)).ConfigureAwait(false);
            erased = await ReadAllAsync(DecisionRecordFile.ReadAsync(path, CancellationToken.None)).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }

        Program.Check(
            kept is [{ Outcomes.Count: 1 }] && string.Equals(kept[0].Record.DecisionId, record.DecisionId, StringComparison.Ordinal) && erased.Count == 0,
            "the public line writers make a file the reader reads, and their erasure line erases, under Native AOT");
    }

    // Adds an outcome and erases through the IDecisionRecorder interface; returns the outcomes the record had and the
    // records left after the erasure.
    private static async Task<(int Outcomes, int AfterErasure)> ThroughTheRecorderInterfaceAsync(IDecisionRecorder recording, InMemoryDecisionRecordSink memory)
    {
        await recording.FlushAsync().ConfigureAwait(false);
        var decisionId = memory.Records[0].Record.DecisionId;
        await recording.AddOutcomeAsync(decisionId, DecisionOutcome.Signal("mailer", At, "opened")).ConfigureAwait(false);
        await recording.FlushAsync().ConfigureAwait(false);
        var outcomes = memory.Records[0].Outcomes.Count;
        await recording.EraseDecisionAsync(decisionId).ConfigureAwait(false);
        await recording.EraseAsync("nobody").ConfigureAwait(false);
        return (outcomes, memory.Records.Count);
    }

    // Writes, adds an outcome and an orphan outcome, which a sink must not throw for, and erases through the
    // IDecisionRecordSink interface.
    private static async Task ThroughTheSinkInterfaceAsync(IDecisionRecordSink sink, DecisionRecord record)
    {
        await sink.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);
        await sink.AddOutcomeAsync(record.DecisionId, DecisionOutcome.Signal("mailer", At, "opened"), CancellationToken.None).ConfigureAwait(false);
        await sink.AddOutcomeAsync("orphan", DecisionOutcome.Signal("mailer", At, "opened"), CancellationToken.None).ConfigureAwait(false);
        await sink.EraseAsync("nobody", CancellationToken.None).ConfigureAwait(false);
        await sink.EraseDecisionAsync(record.DecisionId, CancellationToken.None).ConfigureAwait(false);
    }

    // Records one decision through an in-memory sink and returns its record, which only a recorder can make.
    private static async Task<DecisionRecord> RecordOneAsync()
    {
        var memory = new InMemoryDecisionRecordSink();
        var recorder = new DecisionRecorder(memory);
        using (var client = new FixedResponseClient(PipelineChecks.TriageResponse()).AsBuilder().UseRecording(recorder).Build())
        {
            _ = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);
        }

        await recorder.DisposeAsync().ConfigureAwait(false);
        return memory.Records[0].Record;
    }

    private static async Task<List<RecordedDecision>> ReadAllAsync(IAsyncEnumerable<RecordedDecision> decisions)
    {
        var list = new List<RecordedDecision>();
        await foreach (var decision in decisions.ConfigureAwait(false))
        {
            list.Add(decision);
        }

        return list;
    }
}
