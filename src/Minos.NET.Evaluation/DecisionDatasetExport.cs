using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Minos.Recording;
using Minos.Recording.RecordFormat;

namespace Minos.Evaluation;

/// <summary>
/// Turns recorded decisions, with the answers people confirmed later, into a labelled dataset in format version 1:
/// one item per decision that has at least one confirmed answer that fits its question. Every item is checked with the
/// dataset loader's own rules before it is written, so the output always loads in <see cref="DecisionDataset"/>.
/// </summary>
public static class DecisionDatasetExport
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly byte[] LineFeed = "\n"u8.ToArray();

    /// <summary>
    /// Writes one dataset line per exportable decision, in the order the decisions come. Per decision:
    /// <list type="bullet">
    /// <item>A decision without a confirmed outcome, or one that sent images, is skipped.</item>
    /// <item>A confirmed answer whose key names no question, whose kind differs from the question's, or whose option or
    /// level the question does not have, is dropped before the merge.</item>
    /// <item>The valid answers merge per key: the latest <see cref="DecisionOutcome.At"/> wins, and on an equal time the
    /// outcome later in the file wins. A decision with no valid answer left is skipped.</item>
    /// <item>The item's <c>source</c> lists the distinct sources of the outcomes that supplied a winning answer, in file
    /// order, joined with <c>", "</c>.</item>
    /// <item>An item the dataset loader would reject is skipped, never written.</item>
    /// </list>
    /// The decisions' state is written as recorded. A failure in a stream or a cancellation is thrown, and the output may
    /// hold part of the export by then.
    /// </summary>
    /// <param name="decisions">The decisions, as <see cref="DecisionRecordFile.ReadAsync(string, CancellationToken)"/> returns them.</param>
    /// <param name="dataset">Receives the dataset, UTF-8 JSONL with each line ended by <c>\n</c>.</param>
    /// <param name="signals">Receives the signal outcomes of the exported items, one UTF-8 JSONL line each ended by <c>\n</c>
    /// with <c>id</c>, <c>source</c>, <c>at</c>, <c>name</c>, <c>value</c> and <c>note</c> when set, and <c>version</c> 1, in
    /// file order after their item. <see langword="null"/> only counts them. The signals of a skipped decision are
    /// counted in <see cref="DatasetExportSummary.SignalsSkipped"/>, never written.</param>
    /// <param name="cancellationToken">Cancels the export between decisions.</param>
    /// <returns>The counts of what was read, written and skipped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="decisions"/> or <paramref name="dataset"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<DatasetExportSummary> WriteAsync(
        IAsyncEnumerable<RecordedDecision> decisions, Stream dataset, Stream? signals, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(dataset);
        return WriteCoreAsync(decisions, dataset, signals, cancellationToken);
    }

    private static async Task<DatasetExportSummary> WriteCoreAsync(
        IAsyncEnumerable<RecordedDecision> decisions, Stream dataset, Stream? signals, CancellationToken cancellationToken)
    {
        var counts = new Counts();
        var line = new ArrayBufferWriter<byte>();
        await foreach (var decision in decisions.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.DecisionsRead++;
            line.ResetWrittenCount();
            if (!TryWriteItem(line, decision, counts))
            {
                counts.SignalsSkipped += CountSignals(decision);
                continue;
            }

            // The loader's own rules decide; an item it rejects is counted and left out.
            if (!DecisionDataset.Parse(new MemoryStream(line.WrittenSpan.ToArray(), writable: false)).IsSuccess)
            {
                counts.SkippedInvalidItem++;
                counts.SignalsSkipped += CountSignals(decision);
                continue;
            }

            await dataset.WriteAsync(line.WrittenMemory, cancellationToken).ConfigureAwait(false);
            await dataset.WriteAsync(LineFeed, cancellationToken).ConfigureAwait(false);
            counts.ItemsWritten++;

            foreach (var outcome in decision.Outcomes)
            {
                if (outcome is not SignalOutcome signal)
                {
                    continue;
                }

                if (signals is not null)
                {
                    line.ResetWrittenCount();
                    WriteSignal(line, decision.Record.DecisionId, signal);
                    await signals.WriteAsync(line.WrittenMemory, cancellationToken).ConfigureAwait(false);
                    await signals.WriteAsync(LineFeed, cancellationToken).ConfigureAwait(false);
                }

                counts.SignalsWritten++;
            }
        }

        return new DatasetExportSummary(
            counts.DecisionsRead,
            counts.ItemsWritten,
            counts.SignalsWritten,
            counts.SignalsSkipped,
            counts.SkippedWithoutConfirmedOutcome,
            counts.SkippedWithImages,
            counts.SkippedWithoutValidKey,
            counts.SkippedInvalidItem,
            counts.KeysDropped,
            counts.KeysOverridden);
    }

    private static int CountSignals(RecordedDecision decision)
    {
        var count = 0;
        foreach (var outcome in decision.Outcomes)
        {
            if (outcome is SignalOutcome)
            {
                count++;
            }
        }

        return count;
    }

    private static void WriteSignal(ArrayBufferWriter<byte> line, string decisionId, SignalOutcome signal)
    {
        using var w = new Utf8JsonWriter(line, WriterOptions);
        w.WriteStartObject();
        w.WriteString("id", decisionId);
        w.WriteString("source", signal.Source);
        w.WriteString("at", signal.At);
        w.WriteString("name", signal.Name);
        if (signal.Value is { } value)
        {
            w.WriteNumber("value", value);
        }

        if (signal.Note is not null)
        {
            w.WriteString("note", signal.Note);
        }

        w.WriteNumber("version", 1);
        w.WriteEndObject();
        w.Flush();
    }

    // Checks and merges the decision's confirmed answers and writes its line. Returns false, with the reason counted,
    // for a decision that has no item.
    private static bool TryWriteItem(ArrayBufferWriter<byte> line, RecordedDecision decision, Counts counts)
    {
        var record = decision.Record;
        var confirmed = new List<ConfirmedOutcome>();
        foreach (var outcome in decision.Outcomes)
        {
            if (outcome is ConfirmedOutcome c)
            {
                confirmed.Add(c);
            }
        }

        if (confirmed.Count == 0)
        {
            counts.SkippedWithoutConfirmedOutcome++;
            return false;
        }

        if (record.ImageCount > 0)
        {
            counts.SkippedWithImages++;
            return false;
        }

        // Per key, the winning answer and the index of its outcome. An answer that does not fit is dropped first, so it
        // never takes part in the merge. Outcomes come in file order, so ">=" lets a later one win an equal time.
        var winners = new Dictionary<string, (ConfirmedAnswer Answer, DateTimeOffset At, int Outcome)>(StringComparer.Ordinal);
        var valid = new Dictionary<string, List<ConfirmedAnswer>>(StringComparer.Ordinal);
        for (var i = 0; i < confirmed.Count; i++)
        {
            foreach (var (key, answer) in confirmed[i].Answers)
            {
                if (!Fits(record.Definition, key, answer))
                {
                    counts.KeysDropped++;
                    continue;
                }

                if (!valid.TryGetValue(key, out var answers))
                {
                    valid[key] = answers = [];
                }

                answers.Add(answer);
                if (!winners.TryGetValue(key, out var winner) || confirmed[i].At >= winner.At)
                {
                    winners[key] = (answer, confirmed[i].At, i);
                }
            }
        }

        if (winners.Count == 0)
        {
            counts.SkippedWithoutValidKey++;
            return false;
        }

        foreach (var (key, winner) in winners)
        {
            if (valid[key].Exists(a => a != winner.Answer))
            {
                counts.KeysOverridden++;
            }
        }

        using var w = new Utf8JsonWriter(line, WriterOptions);
        w.WriteStartObject();
        w.WriteString("id", record.DecisionId);
        w.WritePropertyName("state");
        QuestionSetFormat.WriteContent(w, record.State);
        w.WritePropertyName("questions");
        QuestionSetFormat.Write(w, record.Definition);
        w.WriteStartObject("expected");
        foreach (var question in record.Definition.Questions)
        {
            if (winners.TryGetValue(question.Key, out var winner))
            {
                WriteExpected(w, question.Key, winner.Answer);
            }
        }

        w.WriteEndObject();
        w.WriteString("source", Sources(confirmed, winners.Values.Select(v => v.Outcome)));
        w.WriteEndObject();
        w.Flush();
        return true;
    }

    private static bool Fits(QuestionSetDefinition definition, string key, ConfirmedAnswer answer)
    {
        foreach (var question in definition.Questions)
        {
            if (!string.Equals(question.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            if (question.Kind != answer.Kind)
            {
                return false;
            }

            return answer.Kind switch
            {
                QuestionKind.Choice => HasOption(question, answer.ChoiceKey!),
                QuestionKind.Score => answer.ScoreLevel < question.Options.Count,
                _ => true,
            };
        }

        return false;
    }

    private static bool HasOption(QuestionDefinition question, string optionKey)
    {
        foreach (var option in question.Options)
        {
            if (string.Equals(option.Key, optionKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteExpected(Utf8JsonWriter w, string key, ConfirmedAnswer answer)
    {
        switch (answer.Kind)
        {
            case QuestionKind.Noul:
                w.WriteBoolean(key, answer.Noul);
                break;
            case QuestionKind.Choice:
                w.WriteString(key, answer.ChoiceKey);
                break;
            default:
                w.WriteNumber(key, answer.ScoreLevel);
                break;
        }
    }

    // The distinct sources of the outcomes that supplied a winning answer, in file order.
    private static string Sources(List<ConfirmedOutcome> confirmed, IEnumerable<int> winningOutcomes)
    {
        var sources = new List<string>();
        foreach (var index in winningOutcomes.Distinct().Order())
        {
            var source = confirmed[index].Source;
            if (!sources.Contains(source, StringComparer.Ordinal))
            {
                sources.Add(source);
            }
        }

        return string.Join(", ", sources);
    }

    private sealed class Counts
    {
        public int DecisionsRead { get; set; }

        public int ItemsWritten { get; set; }

        public int SignalsWritten { get; set; }

        public int SignalsSkipped { get; set; }

        public int SkippedWithoutConfirmedOutcome { get; set; }

        public int SkippedWithImages { get; set; }

        public int SkippedWithoutValidKey { get; set; }

        public int SkippedInvalidItem { get; set; }

        public int KeysDropped { get; set; }

        public int KeysOverridden { get; set; }
    }
}
