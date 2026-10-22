using System.Buffers;
using System.Reflection;
using static Minos.Recording.Tests.TestRecords;

namespace Minos.Recording.Tests;

/// <summary>The public line writers write the lines the JSONL sink does, and a sink that stores them can export a file.</summary>
public sealed class LineWriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("minos-lines-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    // The same calls as the JSONL sink makes, through the public writers: each line, then a line feed.
    private static byte[] WrittenByTheWriters()
    {
        var buffer = new ArrayBufferWriter<byte>();
        void End() => buffer.Write("\n"u8);
        DecisionRecordFile.WriteRecordLine(buffer, Record("d-1", erasureKey: "customer-7"));
        End();
        DecisionRecordFile.WriteOutcomeLine(buffer, "d-1", Signal("clicked"));
        End();
        DecisionRecordFile.WriteOutcomeLine(buffer, "d-1", Confirmed());
        End();
        DecisionRecordFile.WriteRecordLine(buffer, Record("d-2", erasureKey: "customer-8"));
        End();
        DecisionRecordFile.WriteErasureLine(buffer, "customer-8", null);
        End();
        DecisionRecordFile.WriteRecordLine(buffer, Record("d-3", erasureKey: null));
        End();
        DecisionRecordFile.WriteErasureLine(buffer, null, "d-3");
        End();
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public async Task TheWriters_WriteTheBytesTheJsonlSinkWrites()
    {
        var path = PathOf("sink.jsonl");
        await using (var sink = new JsonlDecisionRecordSink(path))
        {
            var ct = CancellationToken.None;
            await sink.WriteAsync(Record("d-1", erasureKey: "customer-7"), ct);
            await sink.AddOutcomeAsync("d-1", Signal("clicked"), ct);
            await sink.AddOutcomeAsync("d-1", Confirmed(), ct);
            await sink.WriteAsync(Record("d-2", erasureKey: "customer-8"), ct);
            await sink.EraseAsync("customer-8", ct);
            await sink.WriteAsync(Record("d-3", erasureKey: null), ct);
            await sink.EraseDecisionAsync("d-3", ct);
        }

        Assert.Equal(await File.ReadAllBytesAsync(path), WrittenByTheWriters());
    }

    [Fact]
    public async Task EachWriter_WritesOneLine_WithoutALineFeed()
    {
        var buffer = new ArrayBufferWriter<byte>();
        DecisionRecordFile.WriteRecordLine(buffer, Record());
        Assert.NotEqual((byte)'\n', buffer.WrittenSpan[^1]);
        Assert.DoesNotContain((byte)'\n', buffer.WrittenSpan.ToArray());

        buffer.ResetWrittenCount();
        DecisionRecordFile.WriteOutcomeLine(buffer, "d-1", Signal());
        Assert.DoesNotContain((byte)'\n', buffer.WrittenSpan.ToArray());

        buffer.ResetWrittenCount();
        DecisionRecordFile.WriteErasureLine(buffer, "customer-7", null);
        Assert.Equal("""{"kind":"erasure","version":1,"erasureKey":"customer-7"}"""u8.ToArray(), buffer.WrittenSpan.ToArray());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AFileOfTheWrittenLines_ReadsBack()
    {
        var path = PathOf("export.jsonl");
        await File.WriteAllBytesAsync(path, WrittenByTheWriters());

        var problems = new List<DecisionRecordFileProblem>();
        var decisions = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, CancellationToken.None))
        {
            decisions.Add(decision);
        }

        Assert.Empty(problems);
        var only = Assert.Single(decisions);
        Assert.Equal(Line(Record("d-1", erasureKey: "customer-7")), Line(only.Record));
        Assert.IsType<SignalOutcome>(only.Outcomes[0]);
        Assert.IsType<ConfirmedOutcome>(only.Outcomes[1]);
    }

    [Fact]
    public void TheWriters_CheckTheirArguments_BeforeWritingAnything()
    {
        var buffer = new ArrayBufferWriter<byte>();

        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.WriteRecordLine(null!, Record()));
        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.WriteRecordLine(buffer, null!));
        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.WriteOutcomeLine(null!, "d-1", Signal()));
        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.WriteOutcomeLine(buffer, null!, Signal()));
        Assert.Throws<ArgumentException>(() => DecisionRecordFile.WriteOutcomeLine(buffer, string.Empty, Signal()));
        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.WriteOutcomeLine(buffer, "d-1", null!));
        Assert.Throws<ArgumentNullException>(() => DecisionRecordFile.WriteErasureLine(null!, "customer-7", null));

        // Exactly one of the key and the id, and not an empty one.
        Assert.Throws<ArgumentException>(() => DecisionRecordFile.WriteErasureLine(buffer, null, null));
        Assert.Throws<ArgumentException>(() => DecisionRecordFile.WriteErasureLine(buffer, "customer-7", "d-1"));
        Assert.Throws<ArgumentException>(() => DecisionRecordFile.WriteErasureLine(buffer, string.Empty, null));
        Assert.Throws<ArgumentException>(() => DecisionRecordFile.WriteErasureLine(buffer, null, string.Empty));

        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public async Task TheJsonlSink_ForcesAnErasureLineToDisk_AndNotTheOthers()
    {
        var flushes = new List<long>();
        await using var sink = new JsonlDecisionRecordSink(PathOf("flush.jsonl"));
        sink.FlushToDisk = stream =>
        {
            flushes.Add(stream.Length);
            stream.Flush(flushToDisk: true);
        };
        var ct = CancellationToken.None;

        await sink.WriteAsync(Record("d-1"), ct);
        await sink.AddOutcomeAsync("d-1", Signal(), ct);
        Assert.Empty(flushes);

        await sink.EraseAsync("customer-7", ct);
        await sink.EraseDecisionAsync("d-1", ct);

        // Each erasure is on disk before its call returns: forced once its line was written.
        var length = new FileInfo(PathOf("flush.jsonl")).Length;
        Assert.Equal(2, flushes.Count);
        Assert.Equal(length, flushes[1]);
        Assert.True(flushes[0] < flushes[1]);
    }

    [Fact]
    public void ARecordedAnswer_IsAReadOnlyValue_ThatOnlyTheLibraryCreates()
    {
        var type = typeof(RecordedAnswer);

        Assert.True(type.IsValueType);
        Assert.NotNull(type.GetCustomAttribute<System.Runtime.CompilerServices.IsReadOnlyAttribute>());
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetMethod("Deconstruct", BindingFlags.Public | BindingFlags.Instance));
        Assert.All(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), property => Assert.Null(property.SetMethod));
        Assert.Null(type.GetMethod("<Clone>$"));
    }

    [Fact]
    public void ARecordedAnswer_ComparesItsProbabilities_OneByOne()
    {
        var answer = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25]);
        var same = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, new List<double> { 0.75, 0.25 });
        var other = new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.5, 0.5]);

        Assert.True(answer == same);
        Assert.True(answer.Equals((object)same));
        Assert.Equal(answer.GetHashCode(), same.GetHashCode());
        Assert.True(answer != other);
        Assert.Empty(default(RecordedAnswer).Probabilities);
        Assert.Equal(default, new RecordedAnswer());
    }
}
