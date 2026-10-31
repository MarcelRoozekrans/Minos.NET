using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Minos.Recording.RecordFormat;

/// <summary>
/// Writes the lines of a record file: one JSON object per line with a <c>kind</c> of <c>record</c>,
/// <c>outcome</c> or <c>erasure</c> and a <c>version</c> of 1. A record line with an answer, its own or its shadow's,
/// whose probabilities do not come from the model has a <c>version</c> of 2. The caller ends each line with <c>\n</c>.
/// </summary>
internal static class RecordLine
{
    internal const int Version = 1;

    /// <summary>The version of a record line that has an answer with a probability source.</summary>
    internal const int VersionWithSource = 2;

    private const double TicksPerMillisecond = TimeSpan.TicksPerMillisecond;

    /// <summary>Gets the options a line's writer needs: compact, and with the text kept readable instead of escaped.</summary>
    internal static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };


    /// <summary>Writes a record as one line, without the line feed.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="record">The record. Text with an unpaired surrogate is written with U+FFFD in its place, so the write is lossy for such text. A decision id is replaced the same way on a record, an outcome and an erasure, so the ids still match when the file is read.</param>
    internal static void Write(Utf8JsonWriter w, DecisionRecord record)
    {
        w.WriteStartObject();
        w.WriteString("kind", "record");
        var marked = HasMarkedAnswer(record.Answers) || (record.Shadow is { } shadow && HasMarkedAnswer(shadow.Answers));
        w.WriteNumber("version", marked ? VersionWithSource : Version);
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
        WriteUsage(w, record.Usage);
        w.WriteBoolean("cached", record.IsCached);
        w.WritePropertyName("questions");
        QuestionSetFormat.Write(w, record.Definition);
        w.WritePropertyName("state");
        QuestionSetFormat.WriteContent(w, record.State);
        w.WriteNumber("imageCount", record.ImageCount);
        WriteStrings(w, "imageHashes", record.ImageHashes);
        WriteAnswers(w, record.Answers);
        WriteErrorKind(w, record.ErrorKind);
        WriteStrings(w, "refusedKeys", record.RefusedKeys);
        if (record.Shadow is { } candidate)
        {
            w.WriteStartObject("shadow");
            w.WriteString("provider", candidate.Provider);
            WriteOptional(w, "endpoint", candidate.Endpoint);
            WriteOptional(w, "defaultModel", candidate.DefaultModel);
            WriteOptional(w, "requestedModel", candidate.RequestedModel);
            WriteOptional(w, "servedModel", candidate.ServedModel);
            WriteOptional(w, "responseId", candidate.ResponseId);
            WriteOptional(w, "upstreamProvider", candidate.UpstreamProvider);
            w.WriteNumber("latencyMs", candidate.Latency.Ticks / TicksPerMillisecond);
            WriteUsage(w, candidate.Usage);
            WriteAnswers(w, candidate.Answers);
            WriteErrorKind(w, candidate.ErrorKind);
            WriteStrings(w, "refusedKeys", candidate.RefusedKeys);
            w.WriteEndObject();
        }

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
        var scratch = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(scratch, WriterOptions))
        {
            write(writer);
        }

        destination.Write(scratch.WrittenSpan);
    }

    // True when an answer's probabilities are not the model's own, so the line needs the version with sources.
    private static bool HasMarkedAnswer(IReadOnlyList<RecordedAnswer> answers)
    {
        for (var i = 0; i < answers.Count; i++)
        {
            if (answers[i].ProbabilitySource != ProbabilitySource.Model)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteUsage(Utf8JsonWriter w, DecisionUsage? usage)
    {
        if (usage is null)
        {
            return;
        }

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

    private static void WriteAnswers(Utf8JsonWriter w, IReadOnlyList<RecordedAnswer> answers)
    {
        if (answers.Count == 0)
        {
            return;
        }

        w.WriteStartArray("answers");
        foreach (var answer in answers)
        {
            WriteAnswer(w, answer);
        }

        w.WriteEndArray();
    }

    private static void WriteErrorKind(Utf8JsonWriter w, DecisionErrorKind? errorKind)
    {
        if (errorKind is { } kind)
        {
            w.WriteString("errorKind", kind.ToString());
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
        switch (answer.ProbabilitySource)
        {
            case ProbabilitySource.Logprobs:
                w.WriteString("probabilitySource", "logprobs");
                break;
            case ProbabilitySource.Verbalized:
                w.WriteString("probabilitySource", "verbalized");
                break;
        }

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
