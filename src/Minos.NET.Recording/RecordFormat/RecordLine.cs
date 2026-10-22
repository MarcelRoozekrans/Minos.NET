using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Minos.Recording.RecordFormat;

/// <summary>
/// Writes the lines of a record file, version 1: one JSON object per line with a <c>kind</c> of <c>record</c>,
/// <c>outcome</c> or <c>erasure</c> and a <c>version</c> of 1. The caller ends each line with <c>\n</c>.
/// </summary>
internal static class RecordLine
{
    internal const int Version = 1;

    private const double TicksPerMillisecond = TimeSpan.TicksPerMillisecond;

    /// <summary>Gets the options a line's writer needs: compact, and with the text kept readable instead of escaped.</summary>
    internal static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? t_scratch;

    /// <summary>Writes a record as one line, without the line feed.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="record">The record. Text with an unpaired surrogate is written with U+FFFD in its place, so the write is lossy for such text. A decision id is replaced the same way on a record, an outcome and an erasure, so the ids still match when the file is read.</param>
    internal static void Write(Utf8JsonWriter w, DecisionRecord record)
    {
        w.WriteStartObject();
        w.WriteString("kind", "record");
        w.WriteNumber("version", Version);
        w.WriteString("decisionId", record.DecisionId);
        WriteOptional(w, "erasureKey", record.ErasureKey);
        w.WriteString("started", record.Started);
        w.WriteNumber("latencyMs", record.Latency.Ticks / TicksPerMillisecond);
        w.WriteString("provider", record.Provider);
        WriteOptional(w, "endpoint", record.Endpoint);
        WriteOptional(w, "defaultModel", record.DefaultModel);
        WriteOptional(w, "requestedModel", record.RequestedModel);
        WriteOptional(w, "servedModel", record.ServedModel);
        WriteOptional(w, "responseId", record.ResponseId);
        WriteOptional(w, "upstreamProvider", record.UpstreamProvider);
        if (record.Usage is { } usage)
        {
            w.WriteStartObject("usage");
            if (usage.InputTokens is { } input)
            {
                w.WriteNumber("inputTokens", input);
            }

            if (usage.OutputTokens is { } output)
            {
                w.WriteNumber("outputTokens", output);
            }

            if (usage.Cost is { } cost)
            {
                w.WriteNumber("cost", cost);
            }

            w.WriteEndObject();
        }

        w.WriteBoolean("cached", record.IsCached);
        w.WritePropertyName("questions");
        QuestionSetFormat.Write(w, record.Definition);
        w.WritePropertyName("state");
        QuestionSetFormat.WriteContent(w, record.State);
        w.WriteNumber("imageCount", record.ImageCount);
        WriteStrings(w, "imageHashes", record.ImageHashes);
        if (record.Answers.Count > 0)
        {
            w.WriteStartArray("answers");
            foreach (var answer in record.Answers)
            {
                WriteAnswer(w, answer);
            }

            w.WriteEndArray();
        }

        if (record.ErrorKind is { } errorKind)
        {
            w.WriteString("errorKind", errorKind.ToString());
        }

        WriteStrings(w, "refusedKeys", record.RefusedKeys);
        w.WriteEndObject();
    }

    /// <summary>Writes an outcome of the decision <paramref name="decisionId"/> as one line, without the line feed.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="decisionId">The id of the decision the outcome belongs to.</param>
    /// <param name="outcome">The outcome.</param>
    internal static void WriteOutcome(Utf8JsonWriter w, string decisionId, DecisionOutcome outcome)
    {
        w.WriteStartObject();
        w.WriteString("kind", "outcome");
        w.WriteNumber("version", Version);
        w.WriteString("decisionId", decisionId);
        switch (outcome)
        {
            case ConfirmedOutcome confirmed:
                w.WriteString("type", "confirmed");
                w.WriteString("source", confirmed.Source);
                w.WriteString("at", confirmed.At);
                w.WriteStartObject("confirmed");
                foreach (var (key, answer) in confirmed.Answers)
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

                w.WriteEndObject();
                break;
            case SignalOutcome signal:
                w.WriteString("type", "signal");
                w.WriteString("source", signal.Source);
                w.WriteString("at", signal.At);
                w.WriteString("name", signal.Name);
                if (signal.Value is { } value)
                {
                    w.WriteNumber("value", value);
                }

                WriteOptional(w, "note", signal.Note);
                break;
            default:
                throw new ArgumentException("The outcome is not one of the kinds this version writes.", nameof(outcome));
        }

        w.WriteEndObject();
    }

    /// <summary>Writes an erasure as one line, without the line feed.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="erasureKey">The key of the records to erase, or <see langword="null"/>.</param>
    /// <param name="decisionId">The id of the one record to erase, or <see langword="null"/>.</param>
    internal static void WriteErasure(Utf8JsonWriter w, string? erasureKey, string? decisionId)
    {
        if (string.IsNullOrEmpty(erasureKey) && string.IsNullOrEmpty(decisionId))
        {
            throw new ArgumentException("An erasure needs an erasure key or a decision id.", nameof(erasureKey));
        }

        w.WriteStartObject();
        w.WriteString("kind", "erasure");
        w.WriteNumber("version", Version);
        WriteOptional(w, "erasureKey", erasureKey);
        WriteOptional(w, "decisionId", decisionId);
        w.WriteEndObject();
    }

    /// <summary>
    /// Writes one line, without its line feed, to <paramref name="destination"/>. The line is written to a scratch buffer
    /// first and copied over only once it is whole, so a write that throws leaves <paramref name="destination"/> as it was.
    /// </summary>
    /// <param name="destination">Receives the line's bytes.</param>
    /// <param name="write">Writes the line's JSON.</param>
    internal static void WriteTo(IBufferWriter<byte> destination, Action<Utf8JsonWriter> write)
    {
        // One scratch buffer per thread, taken while in use. It is cleared after each line, since a line holds state.
        var scratch = t_scratch ?? new ArrayBufferWriter<byte>();
        t_scratch = null;
        try
        {
            using (var writer = new Utf8JsonWriter(scratch, WriterOptions))
            {
                write(writer);
            }

            destination.Write(scratch.WrittenSpan);
        }
        finally
        {
            scratch.Clear();
            t_scratch = scratch;
        }
    }

    private static void WriteAnswer(Utf8JsonWriter w, RecordedAnswer answer)
    {
        w.WriteStartObject();
        w.WriteString("key", answer.Key);
        switch (answer.Kind)
        {
            case QuestionKind.Noul:
                w.WriteString("kind", "noul");
                w.WriteNumber("probability", answer.Probability);
                break;
            case QuestionKind.Choice:
                w.WriteString("kind", "choice");
                w.WriteNumber("chosenIndex", answer.ChosenIndex);
                break;
            default:
                w.WriteString("kind", "score");
                w.WriteNumber("chosenIndex", answer.ChosenIndex);
                w.WriteNumber("expected", answer.Expected);
                break;
        }

        w.WriteNumber("confidence", answer.Confidence);
        if (answer.Kind != QuestionKind.Noul)
        {
            w.WriteStartArray("probabilities");
            foreach (var probability in answer.Probabilities)
            {
                w.WriteNumberValue(probability);
            }

            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null)
        {
            w.WriteString(name, value);
        }
    }

    private static void WriteStrings(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        w.WriteStartArray(name);
        foreach (var value in values)
        {
            w.WriteStringValue(value);
        }

        w.WriteEndArray();
    }

    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
