using System.Buffers;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Minos.Evaluation;

// Writes the results file, version 1: a header line, then one line per item. LF line endings, no BOM. An answer whose
// probabilities are not the model's own has a "probabilitySource" member; a Model answer has none, so a file with only
// Model answers is the same as before sources were kept. The version stays 1: a reader that does not know the member
// refuses it as unknown, so it cannot drop a source without saying so.
internal static class ResultsWriter
{
    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Refuses what the reader would not accept back, before anything is written or truncated, listing every problem.
    internal static void Validate(EvaluationResults results)
    {
        var problems = ResultsRules.Check(results);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("The results cannot be saved: " + string.Join(" ", problems.Select(p => p.Message)));
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
        if (h.Partial)
        {
            w.WriteBoolean("partial", true);
        }

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
                throw new UnreachableException("An AnswerRecord is made by its factories, which set a defined kind.");
        }

        w.WriteNumber("confidence", a.Confidence);
        if (a.Probability is { } p)
        {
            w.WriteNumber("probability", p);
        }

        if (ResultsFormat.SourceName(a.ProbabilitySource) is { } source)
        {
            w.WriteString("probabilitySource", source);
        }

        w.WriteEndObject();
    }
}

internal static class ResultsFormat
{
    internal const string Kind = "minos-run";
    internal const int Version = 1;

    internal const string Logprobs = "logprobs";

    internal const string Verbalized = "verbalized";

    // The name a source is written as; null for Model, which is written as no member at all.
    internal static string? SourceName(ProbabilitySource source) => source switch
    {
        ProbabilitySource.Logprobs => Logprobs,
        ProbabilitySource.Verbalized => Verbalized,
        _ => null,
    };
}
