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
    /// Rewrites a file with only what a read keeps: the surviving records, each followed by its outcomes, copied as the
    /// bytes they are in the file. Erasure lines, erased records, replaced attempts, ignored outcomes and lines that cannot
    /// be read are gone. A line this reader cannot model but that is not damaged is kept: a line of a kind or an outcome of
    /// a type it does not know, and every member of a record it does not know. When a line is in a format version this reader
    /// does not know, the file is left as it is. The new file replaces the old one in a single move, after it was flushed to
    /// disk, and keeps the old file's permissions on Unix. Do not compact a file that a recorder is still writing to: lines
    /// appended while it runs can be lost.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cancellationToken">Cancels the compaction before the file is replaced.</param>
    /// <returns>The problems that were found in the file; a line that is damaged is not in the compacted file.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static async ValueTask<DecisionRecordFileProblems> CompactAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var temporary = $"{path}.{Guid.NewGuid():N}.compact.tmp";
        var replace = false;
        DecisionRecordFileProblems problems;
        try
        {
            using (var handle = RecordFileReader.Open(path))
            {
                var scan = await RecordFileReader.ScanAsync(handle, cancellationToken).ConfigureAwait(false);
                problems = new DecisionRecordFileProblems([.. scan.Problems]);
                if (!scan.SkippedUnknownVersion)
                {
                    await CopyAsync(handle, scan, path, temporary, cancellationToken).ConfigureAwait(false);
                    replace = true;
                }
            }

            if (replace)
            {
                File.Move(temporary, path, overwrite: true);
            }
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        return problems;
    }

    private static async Task CopyAsync(
        SafeFileHandle source, RecordFileReader.Scan scan, string path, string temporary, CancellationToken cancellationToken)
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

            foreach (var loose in scan.Loose)
            {
                await AppendAsync(source, loose, buffer, cancellationToken).ConfigureAwait(false);
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
