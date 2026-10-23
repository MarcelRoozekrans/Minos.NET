using System.Buffers;
using System.Runtime.CompilerServices;
using Minos.Recording.RecordFormat;

namespace Minos.Recording;

/// <summary>
/// Reads and writes a decision record file: UTF-8 JSONL with one line per record, outcome or erasure. A file with bad
/// lines does not throw. Each problem is a value with its 1-based line number and a message, and the lines around it are
/// still read. A message never holds a decision id or any state.
/// </summary>
public static class DecisionRecordFile
{
    /// <summary>
    /// Reads the decisions in a file, in the order their records first appear. Each decision carries its outcomes in file
    /// order. The rules:
    /// <list type="bullet">
    /// <item>An erasure, by decision id or by erasure key, applies only to the lines before it. A record that comes after it
    /// is a new decision and survives.</item>
    /// <item>A later record of the same decision id replaces the earlier one, and keeps its outcomes.</item>
    /// <item>An outcome with no record before it is ignored and reported.</item>
    /// <item>A line that is not version 1, or whose kind, outcome type, error kind or answer kind this reader does not know,
    /// is reported and skipped.</item>
    /// </list>
    /// Problems are dropped here: use the overload with a collection to get them. The whole file is read before the first
    /// decision is returned.
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

    private static async IAsyncEnumerable<RecordedDecision> ReadCoreAsync(
        string path, ICollection<DecisionRecordFileProblem>? problems, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var decision in await RecordFileReader.ReadAsync(path, problems, cancellationToken).ConfigureAwait(false))
        {
            yield return decision;
        }
    }
}
