using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using Minos.Recording.RecordFormat;

namespace Minos.Recording;

/// <summary>
/// Reads and compacts a decision record file: UTF-8 JSONL with one line per record, outcome or erasure. A file with bad
/// lines does not throw. Each problem is a value with its 1-based line number and a message, and the lines around it are
/// still read. A message never holds a decision id or any state.
/// </summary>
public static class DecisionRecordFile
{
    /// <summary>
    /// Reads the decisions in a file, in the order their records first appear. Each decision carries its outcomes in file
    /// order. The rules, which are the same for a compaction:
    /// <list type="bullet">
    /// <item>An erasure, by decision id or by erasure key, applies only to the lines before it. A record that comes after it
    /// is a new decision and survives.</item>
    /// <item>A record of the same decision id replaces the earlier one when its attempt is higher or equal, so for an equal
    /// attempt the last one wins. A record with a lower attempt than the one already read is ignored.</item>
    /// <item>The outcomes of a decision are kept across its attempts, in file order.</item>
    /// <item>An outcome with no record before it is ignored and reported.</item>
    /// </list>
    /// Problems are dropped here: use the overload with a collection to get them. The file is scanned before the first
    /// decision is returned, and only where its lines sit is kept in memory: each decision is parsed as it is returned.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The decisions. The file is opened when the first one is requested.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist. It is thrown when the first decision is requested.</exception>
    public static IAsyncEnumerable<RecordedDecision> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadCoreAsync(path, null, cancellationToken);
    }

    /// <inheritdoc cref="ReadAsync(string, CancellationToken)"/>
    /// <param name="path">The path of the file.</param>
    /// <param name="problems">Receives the problems, in line order, before the first decision is returned.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static IAsyncEnumerable<RecordedDecision> ReadAsync(
        string path, ICollection<DecisionRecordFileProblem> problems, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(problems);
        return ReadCoreAsync(path, problems, cancellationToken);
    }

    /// <summary>
    /// Writes a record as one line of the file format, the bytes <see cref="JsonlDecisionRecordSink"/> writes for it,
    /// without the line feed that ends it in a file. A sink of your own can store these bytes and export a file later:
    /// end each line with <c>\n</c>, in the order the sink received them, and <see cref="ReadAsync(string, CancellationToken)"/>
    /// reads it.
    /// </summary>
    /// <param name="destination">Receives the line's UTF-8 bytes.</param>
    /// <param name="record">The record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="record"/> is <see langword="null"/>.</exception>
    public static void WriteRecordLine(IBufferWriter<byte> destination, DecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(record);
        RecordLine.WriteTo(destination, w => RecordLine.Write(w, record));
    }

    /// <summary>
    /// Writes an outcome of a decision as one line of the file format, the bytes <see cref="JsonlDecisionRecordSink"/>
    /// writes for it, without the line feed that ends it in a file.
    /// </summary>
    /// <param name="destination">Receives the line's UTF-8 bytes.</param>
    /// <param name="decisionId">The id of the decision the outcome belongs to.</param>
    /// <param name="outcome">The outcome.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/>, <paramref name="decisionId"/> or <paramref name="outcome"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="decisionId"/> is empty.</exception>
    public static void WriteOutcomeLine(IBufferWriter<byte> destination, string decisionId, DecisionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        ArgumentNullException.ThrowIfNull(outcome);
        RecordLine.WriteTo(destination, w => RecordLine.WriteOutcome(w, decisionId, outcome));
    }

    /// <summary>
    /// Writes an erasure as one line of the file format, the bytes <see cref="JsonlDecisionRecordSink"/> writes for it,
    /// without the line feed that ends it in a file. Give exactly one of <paramref name="erasureKey"/> and
    /// <paramref name="decisionId"/>.
    /// </summary>
    /// <param name="destination">Receives the line's UTF-8 bytes.</param>
    /// <param name="erasureKey">The key of the records to erase, or <see langword="null"/> to erase by id.</param>
    /// <param name="decisionId">The id of the decision to erase, or <see langword="null"/> to erase by key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Both or neither of <paramref name="erasureKey"/> and <paramref name="decisionId"/> are given, or the one given is empty.</exception>
    public static void WriteErasureLine(IBufferWriter<byte> destination, string? erasureKey, string? decisionId)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if ((erasureKey is null) == (decisionId is null))
        {
            throw new ArgumentException("An erasure line has exactly one of an erasure key and a decision id.", nameof(erasureKey));
        }

        if (erasureKey is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(erasureKey);
        }
        else
        {
            ArgumentException.ThrowIfNullOrEmpty(decisionId);
        }

        RecordLine.WriteTo(destination, w => RecordLine.WriteErasure(w, erasureKey, decisionId));
    }

    /// <summary>
    /// Rewrites a file with only what a read keeps: the surviving records, each followed by its outcomes, copied as the
    /// bytes they are in the file, so every member of a record is kept, including the ones this reader does not know.
    /// Erasure lines, erased records, replaced attempts, ignored outcomes and lines that cannot be read are gone.
    /// A file this reader cannot fully read is left as it is, byte for byte, and a problem says why: it holds a line in a
    /// format version this reader does not know, a line of a kind it does not know, a record with an error kind or an
    /// answer kind it does not know, or an outcome of a type it does not know that no record explains. Such a line could hold data that an erasure is meant to remove, and dropping the
    /// erasure would keep that data, so nothing is rewritten. The new file replaces the old one in a single step, after it
    /// was flushed to disk, and keeps the old file's permissions: the mode on Unix, the access list and attributes on
    /// Windows. The compaction needs the file to itself: while any sink or read has the file open, it throws
    /// <see cref="IOException"/> and leaves the file as it is. Dispose the recorder and the sink, compact, then open them
    /// again.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cancellationToken">Cancels the compaction before the file is replaced.</param>
    /// <returns>The problems that were found in the file; a line that is damaged is not in the compacted file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="IOException">A sink or a read has the file open, or the file could not be read or replaced.</exception>
    public static ValueTask<DecisionRecordFileProblems> CompactAsync(string path, CancellationToken cancellationToken)
        => CompactAsync(path, null, cancellationToken);

    // The hook runs once the temporary file exists, before anything is copied: it is how a test makes a compaction fail
    // at the point where the temporary file has to be cleaned up.
    internal static async ValueTask<DecisionRecordFileProblems> CompactAsync(
        string path, Func<string, ValueTask>? afterTemporaryCreated, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var temporary = $"{path}.{Guid.NewGuid():N}.compact.tmp";
        var replace = false;
        DecisionRecordFileProblems problems;
        try
        {
            using (var handle = RecordFileReader.OpenExclusive(path))
            {
                var scan = await RecordFileReader.ScanAsync(handle, cancellationToken).ConfigureAwait(false);
                var found = new List<DecisionRecordFileProblem>(scan.Problems);
                var unmodelled = FirstUnmodelledLine(scan);
                if (unmodelled > 0 && !scan.SkippedUnknownVersion)
                {
                    found.Add(new DecisionRecordFileProblem(
                        unmodelled,
                        "The line is not one this reader models, so the file was not compacted: an erasure may be meant to remove what it holds."));
                    found.Sort(static (a, b) => a.Line.CompareTo(b.Line));
                }

                problems = new DecisionRecordFileProblems([.. found]);
                if (!scan.SkippedUnknownVersion && unmodelled == 0)
                {
                    await CopyAsync(handle, scan, path, temporary, afterTemporaryCreated, cancellationToken).ConfigureAwait(false);
                    replace = true;
                    if (!OperatingSystem.IsWindows())
                    {
                        // Still holding the lock: a sink that opens the file meanwhile fails instead of appending to the
                        // file that is about to be replaced.
                        File.Move(temporary, path, overwrite: true);
                        replace = false;
                    }
                }
            }

            if (replace)
            {
                // Keeps the access list and the attributes of the file it replaces. The handle is closed first, because
                // Windows does not replace a file that is open. A sink that opens the file in between does not share
                // deletion, so the replace fails with an IOException and the temporary file is removed.
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        return problems;
    }

    // The line number of the first line this reader does not model and a rewrite would keep or lose, or 0 when there is none.
    private static int FirstUnmodelledLine(RecordFileReader.Scan scan)
    {
        var first = int.MaxValue;
        foreach (var loose in scan.Loose)
        {
            first = Math.Min(first, loose.Line);
        }

        foreach (var decision in scan.Decisions)
        {
            foreach (var outcome in decision.Outcomes)
            {
                if (!outcome.Known)
                {
                    first = Math.Min(first, outcome.Line);
                }
            }
        }

        return first == int.MaxValue ? 0 : first;
    }

    private static async Task CopyAsync(
        SafeFileHandle source, RecordFileReader.Scan scan, string path, string temporary,
        Func<string, ValueTask>? afterTemporaryCreated, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = 0,
        };
        UnixFileMode? mode = null;
        if (!OperatingSystem.IsWindows())
        {
            // The compacted file holds the same decisions, so it is no more public than the file it replaces.
            mode = File.GetUnixFileMode(path);
            options.UnixCreateMode = mode;
        }

        var stream = new FileStream(temporary, options);
        await using (stream.ConfigureAwait(false))
        {
            if (!OperatingSystem.IsWindows() && mode is { } unixMode)
            {
                // The create mode is masked by the umask, so set the exact mode too.
                File.SetUnixFileMode(temporary, unixMode);
            }

            if (afterTemporaryCreated is not null)
            {
                await afterTemporaryCreated(temporary).ConfigureAwait(false);
            }

            var buffer = new ArrayBufferWriter<byte>();
            foreach (var decision in scan.Decisions)
            {
                await AppendAsync(source, decision.Record, buffer, cancellationToken).ConfigureAwait(false);
                foreach (var outcome in decision.Outcomes)
                {
                    await AppendAsync(source, outcome, buffer, cancellationToken).ConfigureAwait(false);
                }

                await DrainAsync(stream, buffer, 1 << 16, cancellationToken).ConfigureAwait(false);
            }

            await DrainAsync(stream, buffer, 0, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
    }

    private static async ValueTask AppendAsync(
        SafeFileHandle source, LineSpan span, ArrayBufferWriter<byte> buffer, CancellationToken cancellationToken)
    {
        buffer.Write(await RecordFileReader.ReadSpanAsync(source, span, cancellationToken).ConfigureAwait(false));
        buffer.Write("\n"u8);
    }

    private static async ValueTask DrainAsync(Stream stream, ArrayBufferWriter<byte> buffer, int threshold, CancellationToken cancellationToken)
    {
        if (buffer.WrittenCount > threshold || (threshold == 0 && buffer.WrittenCount > 0))
        {
            await stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
            buffer.Clear();
        }
    }

    private static async IAsyncEnumerable<RecordedDecision> ReadCoreAsync(
        string path, ICollection<DecisionRecordFileProblem>? problems, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // One handle for both passes: a file that is replaced between them is still read as it was when it was scanned.
        using var handle = RecordFileReader.Open(path);
        var scan = await RecordFileReader.ScanAsync(handle, cancellationToken).ConfigureAwait(false);
        if (problems is not null)
        {
            foreach (var problem in scan.Problems)
            {
                problems.Add(problem);
            }
        }

        foreach (var entry in scan.Decisions)
        {
            var record = RecordFileReader.ParseRecord(await RecordFileReader.ReadSpanAsync(handle, entry.Record, cancellationToken).ConfigureAwait(false));
            var outcomes = new List<DecisionOutcome>(entry.Outcomes.Count);
            foreach (var span in entry.Outcomes)
            {
                if (span.Known)
                {
                    outcomes.Add(RecordFileReader.ParseOutcome(await RecordFileReader.ReadSpanAsync(handle, span, cancellationToken).ConfigureAwait(false)));
                }
            }

            yield return new RecordedDecision(record, DecisionRecord.Freeze(outcomes));
        }
    }
}
