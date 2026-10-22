using System.Buffers;
using System.Runtime.CompilerServices;
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
    /// order. An erasure removes the records it names, a record of a higher attempt replaces one of a lower attempt, and
    /// an outcome with no record before it is ignored. Problems are dropped here: use the overload with a collection to
    /// get them.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The decisions. The file is read when the first one is requested.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist. It is thrown when the first decision is requested.</exception>
    public static IAsyncEnumerable<RecordedDecision> ReadAsync(string path, CancellationToken cancellationToken)
        => ReadCoreAsync(path, null, cancellationToken);

    /// <inheritdoc cref="ReadAsync(string, CancellationToken)"/>
    /// <param name="path">The path of the file.</param>
    /// <param name="problems">Receives the problems, in line order, before the first decision is returned.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static IAsyncEnumerable<RecordedDecision> ReadAsync(
        string path, ICollection<DecisionRecordFileProblem> problems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problems);
        return ReadCoreAsync(path, problems, cancellationToken);
    }

    /// <summary>
    /// Rewrites a file with only what a read keeps: the surviving records, each followed by its outcomes. Erasure lines,
    /// erased records, replaced attempts, ignored outcomes and lines that could not be read are gone. The new file replaces
    /// the old one in a single move. When a line is in a format version this reader does not know, the file is left as it is.
    /// Do not compact a file that a recorder is still writing to: lines appended while it runs can be lost.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cancellationToken">Cancels the compaction before the file is replaced.</param>
    /// <returns>The problems that were found in the file; the lines they name are not in the compacted file.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static async ValueTask<DecisionRecordFileProblems> CompactAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var result = await RecordFileReader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var problems = new DecisionRecordFileProblems([.. result.Problems]);
        if (result.SkippedNewerVersion)
        {
            return problems;
        }

        var temporary = path + ".compact.tmp";
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                foreach (var decision in result.Decisions)
                {
                    buffer.Clear();
                    RecordLine.AppendLine(buffer, w => RecordLine.Write(w, decision.Record));
                    foreach (var outcome in decision.Outcomes)
                    {
                        RecordLine.AppendLine(buffer, w => RecordLine.WriteOutcome(w, decision.Record.DecisionId, outcome));
                    }

                    await stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        return problems;
    }

    private static async IAsyncEnumerable<RecordedDecision> ReadCoreAsync(
        string path, ICollection<DecisionRecordFileProblem>? problems, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var result = await RecordFileReader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (problems is not null)
        {
            foreach (var problem in result.Problems)
            {
                problems.Add(problem);
            }
        }

        foreach (var decision in result.Decisions)
        {
            yield return decision;
        }
    }
}
