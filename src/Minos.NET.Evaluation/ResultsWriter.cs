using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Minos.Evaluation;

// Writes the results file, version 1: a header line, then one line per item. LF line endings, no BOM.
internal static class ResultsWriter
{
    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Refuses what the reader would not accept back, before anything is written or truncated. The value rules are
    // the reader's, from ResultsRules.
    internal static void Validate(EvaluationResults results)
    {
        var header = results.Header;
        if (string.IsNullOrEmpty(header.DatasetName))
        {
            throw new InvalidOperationException("Header DatasetName must not be null or empty.");
        }

        if (header.ItemCount != results.Items.Count)
        {
            throw new InvalidOperationException($"Header ItemCount is {header.ItemCount}, but there are {results.Items.Count} items.");
        }

        if (header.Parallelism < ResultsRules.MinParallelism)
        {
            throw new InvalidOperationException($"Header Parallelism must be at least {ResultsRules.MinParallelism}.");
        }

        Text("Header", "DatasetName", header.DatasetName);
        Text("Header", "ProviderName", header.ProviderName);
        Text("Header", "Endpoint", header.Endpoint?.OriginalString);
        Text("Header", "DefaultModel", header.DefaultModel);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in results.Items)
        {
            Text($"Item '{item.Id}'", "Id", item.Id);
            Text($"Item '{item.Id}'", "Model", item.Model);
            Text($"Item '{item.Id}'", "ResponseId", item.ResponseId);
            if (item.Error is { } error)
            {
                Text($"Item '{item.Id}'", "Error.Kind", error.Kind);
                Text($"Item '{item.Id}'", "Error.Message", error.Message);
            }

            if (!ids.Add(item.Id))
            {
                throw new InvalidOperationException($"Item '{item.Id}': the id appears twice.");
            }

            if (item.Line < ResultsRules.MinLine)
            {
                throw new InvalidOperationException($"Item '{item.Id}': Line must be at least {ResultsRules.MinLine}.");
            }

            Require(item.Id, "latencyMs", ResultsRules.IsNonNegative(item.LatencyMs), "a non-negative finite number");
            Require(item.Id, "cost", item.Cost is not { } cost || ResultsRules.IsNonNegative(cost), "a non-negative finite number, or null when unknown");
            Require(item.Id, "inputTokens", item.InputTokens is not { } input || input >= ResultsRules.MinTokens, "non-negative");
            Require(item.Id, "outputTokens", item.OutputTokens is not { } output || output >= ResultsRules.MinTokens, "non-negative");

            if ((item.Answers is null) == (item.Error is null))
            {
                throw new InvalidOperationException($"Item '{item.Id}': exactly one of Answers and Error must be set.");
            }

            if (item.Answers is null)
            {
                continue;
            }

            Require(item.Id, "Answers", item.Answers.Count > 0, "not empty");
            foreach (var (key, answer) in item.Answers)
            {
                var member = $"Answers[{key}]";
                Text($"Item '{item.Id}'", member + " key", key);
                Text($"Item '{item.Id}'", member + ".ChoiceKey", answer.ChoiceKey);
                Require(item.Id, member + ".Confidence", ResultsRules.IsUnit(answer.Confidence), "a number from 0 to 1");
                Require(item.Id, member + ".Probability", answer.Probability is not { } probability || ResultsRules.IsUnit(probability), "a number from 0 to 1");
                Require(item.Id, member + ".ChoiceKey", answer.Kind != QuestionKind.Choice || answer.ChoiceKey is not null, "set for a Choice answer");
                Require(item.Id, member + ".Probability", answer.Kind != QuestionKind.Noul || answer.Probability is not null, "set for a Noul answer");
                Require(item.Id, member + ".ScoreLevel", answer.Kind != QuestionKind.Score || answer.ScoreLevel >= ResultsRules.MinScoreLevel, "non-negative");
            }
        }
    }

    private static void Text(string owner, string member, string? text)
    {
        if (!ResultsRules.IsValidText(text))
        {
            throw new InvalidOperationException($"{owner}: {member} holds an unpaired surrogate and cannot be written as UTF-8.");
        }
    }

    private static void Require(string id, string member, bool ok, string rule)
    {
        if (!ok)
        {
            throw new InvalidOperationException($"Item '{id}': {member} must be {rule}.");
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
