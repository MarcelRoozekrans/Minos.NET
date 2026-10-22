using static Minos.Recording.Tests.TestRecords;

namespace Minos.Recording.Tests;

public sealed class SinkTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("minos-sink-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // The same calls, for every sink: records, a retried one, outcomes, and both kinds of erasure, one of which comes
    // before a record it must not touch.
    private static async Task PlayAsync(IDecisionRecordSink sink)
    {
        var ct = CancellationToken.None;
        await sink.WriteAsync(Record("d-1", erasureKey: "customer-7"), ct);
        await sink.WriteAsync(Record("d-2", erasureKey: "customer-8"), ct);
        await sink.AddOutcomeAsync("d-2", Signal("clicked"), ct);
        await sink.WriteAsync(Record("d-2", erasureKey: "customer-8", attempt: 1, state: "Retried."), ct);
        await sink.AddOutcomeAsync("d-2", Confirmed(), ct);
        await sink.WriteAsync(Record("d-3", erasureKey: null), ct);
        await sink.AddOutcomeAsync("d-3", Signal("complained"), ct);
        await sink.WriteAsync(Record("d-4", erasureKey: "customer-7"), ct);
        await sink.EraseAsync("customer-7", ct);
        await sink.AddOutcomeAsync("d-1", Signal("late"), ct);
        await sink.WriteAsync(Record("d-5", erasureKey: "customer-7"), ct);
        await sink.WriteAsync(Record("d-6", erasureKey: null), ct);
        await sink.EraseDecisionAsync("d-6", ct);
        await sink.WriteAsync(Record("d-2", erasureKey: "customer-8", attempt: 0, state: "A stale attempt."), ct);
        await sink.AddOutcomeAsync("d-9", Signal("orphan"), ct);
    }

    [Fact]
    public async Task TheInMemorySink_AppliesOutcomesAndErasures()
    {
        var sink = new InMemoryDecisionRecordSink();
        await PlayAsync(sink);

        var records = sink.Records;

        Assert.Equal(["d-2", "d-3", "d-5"], records.Select(r => r.Record.DecisionId));
        Assert.Equal(1, records[0].Record.Attempt);
        Assert.Equal(DecisionContent.FromString("Retried."), records[0].Record.State);
        Assert.Equal(["clicked", null], records[0].Outcomes.Select(o => (o as SignalOutcome)?.Name));
        Assert.IsType<ConfirmedOutcome>(records[0].Outcomes[1]);
        Assert.Equal(["complained"], records[1].Outcomes.Select(o => ((SignalOutcome)o).Name));
        Assert.Empty(records[2].Outcomes);
    }

    [Fact]
    public async Task TheInMemorySinksRecords_AreASnapshot()
    {
        var sink = new InMemoryDecisionRecordSink();
        await sink.WriteAsync(Record("d-1"), CancellationToken.None);
        var before = sink.Records;

        await sink.AddOutcomeAsync("d-1", Signal(), CancellationToken.None);
        await sink.EraseDecisionAsync("d-1", CancellationToken.None);

        var only = Assert.Single(before);
        Assert.Empty(only.Outcomes);
        Assert.Empty(sink.Records);
    }

    [Fact]
    public async Task TheJsonlFile_ReadsBackAsWhatTheInMemorySinkHolds()
    {
        var path = Path.Combine(_directory, "decisions.jsonl");
        var memory = new InMemoryDecisionRecordSink();
        await PlayAsync(memory);
        await using (var file = new JsonlDecisionRecordSink(path))
        {
            await PlayAsync(file);
        }

        var problems = new List<DecisionRecordFileProblem>();
        var read = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, CancellationToken.None))
        {
            read.Add(decision);
        }

        Assert.Equal(Lines(memory.Records), Lines(read));
        // The one problem is the orphan outcome, which both sides ignore.
        Assert.Single(problems);
    }

    [Fact]
    public async Task TheJsonlSink_Appends_AndFlushesEachLine()
    {
        var path = Path.Combine(_directory, "decisions.jsonl");
        await using (var first = new JsonlDecisionRecordSink(path))
        {
            await first.WriteAsync(Record("d-1"), CancellationToken.None);
        }

        await using var second = new JsonlDecisionRecordSink(path);
        await second.WriteAsync(Record("d-2"), CancellationToken.None);
        await second.EraseAsync("someone", CancellationToken.None);

        // Read while the sink is still open: each line is already in the file.
        var lines = await ReadSharedAsync(path);
        Assert.Equal(3, lines.Length);
        Assert.Equal(Line(Record("d-1")), lines[0]);
        Assert.Equal(Line(Record("d-2")), lines[1]);
        Assert.Equal("""{"kind":"erasure","version":1,"erasureKey":"someone"}""", lines[2]);
    }

    [Fact]
    public async Task AnErasure_Applies_AfterReopeningAFileWhoseLastLineWasCut()
    {
        var path = Path.Combine(_directory, "decisions.jsonl");
        await using (var sink = new JsonlDecisionRecordSink(path))
        {
            await sink.WriteAsync(Record("d-1", erasureKey: "victim"), CancellationToken.None);
        }

        // A crash left part of a line.
        await File.AppendAllTextAsync(path, """{"kind":"record","version":1,"decisionId":"d-""");
        await using (var sink = new JsonlDecisionRecordSink(path))
        {
            await sink.EraseAsync("victim", CancellationToken.None);
            await sink.WriteAsync(Record("d-2", erasureKey: null), CancellationToken.None);
        }

        var problems = new List<DecisionRecordFileProblem>();
        var read = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, CancellationToken.None))
        {
            read.Add(decision);
        }

        Assert.Equal(["d-2"], read.Select(d => d.Record.DecisionId));
        Assert.Equal(2, Assert.Single(problems).Line);
    }

    [Fact]
    public async Task AWriteThatFailed_IsFollowedByALineOfItsOwn()
    {
        var path = Path.Combine(_directory, "decisions.jsonl");
        await using (var sink = new JsonlDecisionRecordSink(path))
        {
            await sink.WriteAsync(Record("d-1"), CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.WriteAsync(Record("d-x"), new CancellationToken(canceled: true)).AsTask());
            await sink.WriteAsync(Record("d-2"), CancellationToken.None);
        }

        // The failed write may have left part of its line; the next one starts on a line of its own.
        Assert.Equal(Line(Record("d-1")) + "\n\n" + Line(Record("d-2")) + "\n", await File.ReadAllTextAsync(path));
        var read = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, CancellationToken.None))
        {
            read.Add(decision);
        }

        Assert.Equal(["d-1", "d-2"], read.Select(d => d.Record.DecisionId));
    }

    [Fact]
    public async Task TheInMemorySink_ChecksItsArguments()
    {
        var sink = new InMemoryDecisionRecordSink();
        await Assert.ThrowsAsync<ArgumentException>(() => sink.AddOutcomeAsync("", Signal(), CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => sink.EraseAsync("", CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => sink.EraseDecisionAsync("", CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.EraseAsync(null!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task TheJsonlSink_MakesAFileOnlyItsOwnerCanRead()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, "private.jsonl");
        await using (var sink = new JsonlDecisionRecordSink(path))
        {
            await sink.WriteAsync(Record(), CancellationToken.None);
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task TheJsonlSink_ThrowsAfterDispose_AndChecksItsArguments()
    {
        Assert.Throws<ArgumentException>(() => new JsonlDecisionRecordSink(""));
        var sink = new JsonlDecisionRecordSink(Path.Combine(_directory, "d.jsonl"));
        await sink.DisposeAsync();
        await sink.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => sink.WriteAsync(Record(), CancellationToken.None).AsTask());
    }

    private static async Task<string[]> ReadSharedAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        return text.TrimEnd('\n').Split('\n');
    }
}
