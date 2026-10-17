using System.Globalization;
using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Evaluation;

// Reads the results file, version 1, reporting what is wrong as line-numbered problems.
internal static class ResultsReader
{
    internal static Result<EvaluationResults, DatasetProblems> Read(ILineSource lines)
    {
        var problems = new List<DatasetProblem>();
        var items = new List<ItemResult>();
        RunHeader? header = null;
        var lineNumber = 0;
        var seenFirst = false;

        while (lines.TryReadLine(out var line))
        {
            lineNumber++;
            if (line is null)
            {
                problems.Add(new DatasetProblem(lineNumber, "The line is not valid UTF-8."));
                seenFirst = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                problems.Add(new DatasetProblem(lineNumber, JsonProblem(ex)));
                seenFirst = true;
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!seenFirst)
                {
                    seenFirst = true;
                    header = ReadHeader(root, lineNumber, problems);
                }
                else
                {
                    var item = ReadItem(root, lineNumber, problems);
                    if (item is not null)
                    {
                        items.Add(item);
                    }
                }
            }
        }

        if (!seenFirst)
        {
            problems.Add(new DatasetProblem(1, "The file is empty: it has no header line."));
        }
        else if (header is not null && header.ItemCount != items.Count && problems.Count == 0)
        {
            problems.Add(new DatasetProblem(1, $"The header says {header.ItemCount.ToString(CultureInfo.InvariantCulture)} items, but the file has {items.Count.ToString(CultureInfo.InvariantCulture)}."));
        }

        if (problems.Count > 0 || header is null)
        {
            return Result<EvaluationResults, DatasetProblems>.Failure(new DatasetProblems([.. problems]));
        }

        return Result<EvaluationResults, DatasetProblems>.Success(new EvaluationResults(header, items));
    }

    private static string JsonProblem(JsonException ex)
    {
        var message = ex.Message;
        var cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (cut >= 0)
        {
            message = message[..cut];
        }

        return ex.BytePositionInLine is { } position
            ? $"The line is not valid JSON at byte {position.ToString(CultureInfo.InvariantCulture)}: {message}"
            : $"The line is not valid JSON: {message}";
    }

    private static RunHeader? ReadHeader(JsonElement root, int line, List<DatasetProblem> problems)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new DatasetProblem(line, "The header line must be a JSON object."));
            return null;
        }

        var before = problems.Count;
        var r = new Fields(root, line, problems);
        var kind = r.String("kind", required: true);
        if (kind is not null && !string.Equals(kind, ResultsFormat.Kind, StringComparison.Ordinal))
        {
            problems.Add(new DatasetProblem(line, $"The file's kind is '{kind}', not '{ResultsFormat.Kind}'."));
        }

        var version = r.Int("version");
        if (version is { } v && v != ResultsFormat.Version)
        {
            problems.Add(new DatasetProblem(line, $"The file's version is {v.ToString(CultureInfo.InvariantCulture)}; only version {ResultsFormat.Version.ToString(CultureInfo.InvariantCulture)} is supported."));
        }

        var dataset = r.String("dataset", required: true);
        var itemCount = r.Int("itemCount");
        var provider = r.String("providerName");
        var endpointText = r.String("endpoint");
        var model = r.String("defaultModel");
        var started = r.Time("started");
        var finished = r.Time("finished");
        var parallelism = r.Int("parallelism");

        Uri? endpoint = null;
        if (endpointText is not null && !Uri.TryCreate(endpointText, UriKind.RelativeOrAbsolute, out endpoint))
        {
            problems.Add(new DatasetProblem(line, "Member 'endpoint' is not a valid URI."));
        }

        if (problems.Count > before || dataset is null || itemCount is null || started is null || finished is null || parallelism is null)
        {
            return null;
        }

        return new RunHeader(dataset, itemCount.Value, provider, endpoint, model, started.Value, finished.Value, parallelism.Value);
    }

    private static ItemResult? ReadItem(JsonElement root, int line, List<DatasetProblem> problems)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new DatasetProblem(line, "The line must be a JSON object."));
            return null;
        }

        var before = problems.Count;
        var r = new Fields(root, line, problems);
        var id = r.String("id", required: true);
        var itemLine = r.Int("line");
        var latency = r.Double("latencyMs");
        var cached = r.Bool("cached");
        var model = r.String("model");
        var inputTokens = r.Int("inputTokens", required: false);
        var outputTokens = r.Int("outputTokens", required: false);
        var cost = r.Double("cost", required: false);
        var responseId = r.String("responseId");

        IReadOnlyDictionary<string, AnswerRecord>? answers = null;
        ItemError? error = null;
        var hasAnswers = root.TryGetProperty("answers", out var answersElement) && answersElement.ValueKind != JsonValueKind.Null;
        var hasError = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null;
        if (hasAnswers == hasError)
        {
            problems.Add(new DatasetProblem(line, "Exactly one of 'answers' and 'error' must be set."));
        }
        else if (hasAnswers)
        {
            answers = ReadAnswers(answersElement, line, problems);
        }
        else
        {
            error = ReadError(errorElement, line, problems);
        }

        if (problems.Count > before || id is null || itemLine is null || latency is null || cached is null)
        {
            return null;
        }

        return new ItemResult(id, itemLine.Value, latency.Value, cached.Value, model, inputTokens, outputTokens, cost, responseId, answers, error);
    }

    private static Dictionary<string, AnswerRecord>? ReadAnswers(JsonElement element, int line, List<DatasetProblem> problems)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new DatasetProblem(line, "Member 'answers' must be an object."));
            return null;
        }

        var before = problems.Count;
        var answers = new Dictionary<string, AnswerRecord>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var answer = ReadAnswer(property.Name, property.Value, line, problems);
            if (answer is not null && !answers.TryAdd(property.Name, answer))
            {
                problems.Add(new DatasetProblem(line, $"Answer '{property.Name}' appears twice."));
            }
        }

        return problems.Count > before ? null : answers;
    }

    private static AnswerRecord? ReadAnswer(string key, JsonElement element, int line, List<DatasetProblem> problems)
    {
        var where = $"answers.{key}";
        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new DatasetProblem(line, $"Member '{where}' must be an object."));
            return null;
        }

        var before = problems.Count;
        var r = new Fields(element, line, problems, where + ".");
        var kind = r.String("kind", required: true);
        var confidence = r.Double("confidence");
        double? probability = r.Double("probability", required: false);
        if (kind is null || !element.TryGetProperty("value", out var value))
        {
            if (kind is not null)
            {
                problems.Add(new DatasetProblem(line, $"Member '{where}.value' is missing."));
            }

            return null;
        }

        AnswerRecord? record = null;
        switch (kind)
        {
            case "noul":
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    record = new AnswerRecord(QuestionKind.Noul, value.GetBoolean(), probability, null, 0, confidence ?? 0);
                }
                else
                {
                    problems.Add(new DatasetProblem(line, $"Member '{where}.value' must be true or false."));
                }

                break;
            case "choice":
                if (value.ValueKind == JsonValueKind.String)
                {
                    record = new AnswerRecord(QuestionKind.Choice, false, probability, value.GetString(), 0, confidence ?? 0);
                }
                else
                {
                    problems.Add(new DatasetProblem(line, $"Member '{where}.value' must be a string."));
                }

                break;
            case "score":
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var level))
                {
                    record = new AnswerRecord(QuestionKind.Score, false, probability, null, level, confidence ?? 0);
                }
                else
                {
                    problems.Add(new DatasetProblem(line, $"Member '{where}.value' must be an integer."));
                }

                break;
            default:
                problems.Add(new DatasetProblem(line, $"Member '{where}.kind' is '{kind}', not noul, choice or score."));
                break;
        }

        return problems.Count > before ? null : record;
    }

    private static ItemError? ReadError(JsonElement element, int line, List<DatasetProblem> problems)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new DatasetProblem(line, "Member 'error' must be an object."));
            return null;
        }

        var before = problems.Count;
        var r = new Fields(element, line, problems, "error.");
        var kind = r.String("kind", required: true);
        var status = r.Int("statusCode", required: false);
        var message = r.String("message", required: true);
        return problems.Count > before || kind is null || message is null ? null : new ItemError(kind, status, message);
    }

    // Typed reads of an object's members; a wrong or missing member is a problem on the line and reads as null.
    private readonly struct Fields(JsonElement element, int line, List<DatasetProblem> problems, string prefix = "")
    {
        internal string? String(string name, bool required = false)
        {
            if (!Find(name, required, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            Bad(name, "a string");
            return null;
        }

        internal int? Int(string name, bool required = true)
        {
            if (!Find(name, required, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            Bad(name, "an integer");
            return null;
        }

        internal double? Double(string name, bool required = true)
        {
            if (!Find(name, required, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDouble();
            }

            Bad(name, "a number");
            return null;
        }

        internal bool? Bool(string name)
        {
            if (!Find(name, true, out var value))
            {
                return null;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            Bad(name, "true or false");
            return null;
        }

        internal DateTimeOffset? Time(string name)
        {
            if (!Find(name, true, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time))
            {
                return time;
            }

            Bad(name, "an ISO 8601 timestamp");
            return null;
        }

        // A missing member is a problem only when required; a null member counts as missing.
        private bool Find(string name, bool required, out JsonElement value)
        {
            if (element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
            {
                return true;
            }

            if (required)
            {
                problems.Add(new DatasetProblem(line, $"Member '{prefix}{name}' is missing."));
            }

            return false;
        }

        private void Bad(string name, string expected)
        {
            problems.Add(new DatasetProblem(line, $"Member '{prefix}{name}' must be {expected}."));
        }
    }
}
