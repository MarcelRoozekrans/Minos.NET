using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Minos.Evaluation;

// Writes the results file, version 1: a header line, then one line per item. LF line endings, no BOM.
internal static class ResultsWriter
{
    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Refuses records the reader would not accept back, before anything is written or truncated.
    internal static void Validate(EvaluationResults results)
    {
        foreach (var item in results.Items)
        {
            Finite(item.Id, "latencyMs", item.LatencyMs);
            if (item.LatencyMs < 0)
            {
                throw new InvalidOperationException($"Item '{item.Id}': latencyMs must not be negative.");
            }

            if (item.Cost is { } cost)
            {
                Finite(item.Id, "cost", cost);
                if (cost < 0)
                {
                    throw new InvalidOperationException($"Item '{item.Id}': cost must not be negative; use null for an unknown cost.");
                }
            }

            if ((item.Answers is null) == (item.Error is null))
            {
                throw new InvalidOperationException($"Item '{item.Id}': exactly one of Answers and Error must be set.");
            }

            if (item.Answers is null)
            {
                continue;
            }

            if (item.Answers.Count == 0)
            {
                throw new InvalidOperationException($"Item '{item.Id}': Answers must not be empty.");
            }

            foreach (var (key, answer) in item.Answers)
            {
                var member = $"Answers[{key}]";
                Finite(item.Id, member + ".Confidence", answer.Confidence);
                if (answer.Probability is { } probability)
                {
                    Finite(item.Id, member + ".Probability", probability);
                }

                if (answer.Kind == QuestionKind.Choice && answer.ChoiceKey is null)
                {
                    throw new InvalidOperationException($"Item '{item.Id}': {member}.ChoiceKey must not be null for a Choice answer.");
                }

                if (answer.Kind == QuestionKind.Noul && answer.Probability is null)
                {
                    throw new InvalidOperationException($"Item '{item.Id}': {member}.Probability must be set for a Noul answer.");
                }
            }
        }
    }

    private static void Finite(string id, string member, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException($"Item '{id}': {member} must be a finite number.");
        }
    }

    internal static void Write(EvaluationResults results, Stream stream)
    {
        Validate(results);
        var buffer = new ArrayBufferWriter<byte>();
        WriteLine(buffer, stream, w => WriteHeader(w, results.Header));
        foreach (var item in results.Items)
        {
            WriteLine(buffer, stream, w => WriteItem(w, item));
        }

        stream.Flush();
    }

    private static void WriteLine(ArrayBufferWriter<byte> buffer, Stream stream, Action<Utf8JsonWriter> write)
    {
        buffer.ResetWrittenCount();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            write(writer);
        }

        buffer.GetSpan(1)[0] = (byte)'\n';
        buffer.Advance(1);
        stream.Write(buffer.WrittenSpan);
    }

    private static void WriteHeader(Utf8JsonWriter w, RunHeader h)
    {
        w.WriteStartObject();
        w.WriteString("kind", ResultsFormat.Kind);
        w.WriteNumber("version", ResultsFormat.Version);
        w.WriteString("dataset", h.DatasetName);
        w.WriteNumber("itemCount", h.ItemCount);
        w.WriteString("providerName", h.ProviderName);
        w.WriteString("endpoint", h.Endpoint?.OriginalString);
        w.WriteString("defaultModel", h.DefaultModel);
        w.WriteString("started", h.Started.ToUniversalTime());
        w.WriteString("finished", h.Finished.ToUniversalTime());
        w.WriteNumber("parallelism", h.Parallelism);
        w.WriteEndObject();
    }

    private static void WriteItem(Utf8JsonWriter w, ItemResult item)
    {
        w.WriteStartObject();
        w.WriteString("id", item.Id);
        w.WriteNumber("line", item.Line);
        w.WriteNumber("latencyMs", item.LatencyMs);
        w.WriteBoolean("cached", item.Cached);
        w.WriteString("model", item.Model);
        WriteNumber(w, "inputTokens", item.InputTokens);
        WriteNumber(w, "outputTokens", item.OutputTokens);
        if (item.Cost is { } cost)
        {
            w.WriteNumber("cost", cost);
        }
        else
        {
            w.WriteNull("cost");
        }

        w.WriteString("responseId", item.ResponseId);
        if (item.Answers is { } answers)
        {
            w.WriteStartObject("answers");
            foreach (var (key, answer) in answers)
            {
                WriteAnswer(w, key, answer);
            }

            w.WriteEndObject();
        }

        if (item.Error is { } error)
        {
            w.WriteStartObject("error");
            w.WriteString("kind", error.Kind);
            WriteNumber(w, "statusCode", error.StatusCode);
            w.WriteString("message", error.Message);
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static void WriteAnswer(Utf8JsonWriter w, string key, AnswerRecord a)
    {
        w.WriteStartObject(key);
        switch (a.Kind)
        {
            case QuestionKind.Noul:
                w.WriteString("kind", "noul");
                w.WriteBoolean("value", a.Noul);
                break;
            case QuestionKind.Choice:
                w.WriteString("kind", "choice");
                w.WriteString("value", a.ChoiceKey);
                break;
            case QuestionKind.Score:
                w.WriteString("kind", "score");
                w.WriteNumber("value", a.ScoreLevel);
                break;
            default:
                throw new InvalidOperationException("Unknown question kind " + a.Kind + ".");
        }

        w.WriteNumber("confidence", a.Confidence);
        if (a.Probability is { } p)
        {
            w.WriteNumber("probability", p);
        }

        w.WriteEndObject();
    }
}

internal static class ResultsFormat
{
    internal const string Kind = "minos-run";
    internal const int Version = 1;
}
