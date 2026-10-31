using static Minos.Recording.Tests.TestRecords;

namespace Minos.Recording.Tests;

public sealed class SinkTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("minos-sink-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // The same calls, for every sink: records, one written again, outcomes, and both kinds of erasure, one of which comes
    // before a record it must not touch.
    private static async Task PlayAsync(IDecisionRecordSink sink)
    {
        var ct = CancellationToken.None;
        await sink.WriteAsync(Record("d-1", erasureKey: "customer-7"), ct);
        await sink.WriteAsync(Record("d-2", erasureKey: "customer-8"), ct);
        await sink.AddOutcomeAsync("d-2", Signal("clicked"), ct);
        await sink.WriteAsync(Record("d-2", erasureKey: "customer-8", state: "Written again."), ct);
        await sink.AddOutcomeAsync("d-2", Confirmed(), ct);
        await sink.WriteAsync(Record("d-3", erasureKey: null), ct);
        await sink.AddOutcomeAsync("d-3", Signal("complained"), ct);
        await sink.WriteAsync(Record("d-4", erasureKey: "customer-7"), ct);
        await sink.EraseAsync("customer-7", ct);
        await sink.AddOutcomeAsync("d-1", Signal("late"), ct);
        await sink.WriteAsync(Record("d-5", erasureKey: "customer-7"), ct);
        await sink.WriteAsync(Record("d-6", erasureKey: null), ct);
        await sink.EraseDecisionAsync("d-6", ct);
        await sink.AddOutcomeAsync("d-9", Signal("orphan"), ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecondWrite_KeepsOutcomesAddedBetween(bool toFile)
    {
        var ct = CancellationToken.None;
        var path = Path.Combine(_directory, "shadow.jsonl");
        var memory = new InMemoryDecisionRecordSink();
        await using var file = new JsonlDecisionRecordSink(path);
        IDecisionRecordSink sink = toFile ? file : memory;
        var record = Record("d-1");
        var shadow = new ShadowResult("anthropic", null, null, null, "candidate-served", null, null, TimeSpan.FromMilliseconds(40), null, [], DecisionErrorKind.Timeout, []);

        await sink.WriteAsync(record, ct);
        await sink.AddOutcomeAsync("d-1", Signal("clicked"), ct);
        await sink.WriteAsync(record.WithShadow(shadow), ct);

        List<RecordedDecision> decisions = [];
        if (toFile)
        {
            await file.DisposeAsync();
            await foreach (var decision in DecisionRecordFile.ReadAsync(path, ct))
            {
                decisions.Add(decision);
            }
        }
        else
        {
            decisions.AddRange(memory.Records);
        }

        var only = Assert.Single(decisions);
        Assert.Equal(DecisionErrorKind.Timeout, only.Record.Shadow!.ErrorKind);
        Assert.Equal("clicked", ((SignalOutcome)Assert.Single(only.Outcomes)).Name);
    }

    [Fact]
    public async Task TheInMemorySink_AppliesOutcomesAndErasures()
    {
        var sink = new InMemoryDecisionRecordSink();
        await PlayAsync(sink);

        var records = sink.Records;

        Assert.Equal(["d-2", "d-3", "d-5"], records.Select(r => r.Record.DecisionId));
        Assert.Equal(DecisionContent.FromString("Written again."), records[0].Record.State);
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
