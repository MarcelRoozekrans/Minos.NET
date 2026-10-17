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
        var itemLines = 0;
        int? declared = null;
        var ids = new HashSet<string>(StringComparer.Ordinal);

        while (lines.TryReadLine(out var line))
        {
            lineNumber++;
            if (line is null)
            {
                problems.Add(new DatasetProblem(lineNumber, "The line is not valid UTF-8."));
                itemLines += seenFirst ? 1 : 0;
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
                itemLines += seenFirst ? 1 : 0;
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
                    declared = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("itemCount", out var count) && count.TryGetInt32(out var n) ? n : null;
                }
                else
                {
                    itemLines++;
                    var item = ReadItem(root, lineNumber, problems);
                    if (item is not null)
                    {
                        if (!ids.Add(item.Id))
                        {
                            problems.Add(new DatasetProblem(lineNumber, $"The id '{item.Id}' appears twice."));
                        }
                        else
                        {
                            items.Add(item);
                        }
                    }
                }
            }
        }

        if (!seenFirst)
        {
            problems.Add(new DatasetProblem(1, "The file is empty: it has no header line."));
        }
        else if (declared is { } count && count != itemLines)
        {
            problems.Add(new DatasetProblem(1, $"The header says {count.ToString(CultureInfo.InvariantCulture)} items, but the file has {itemLines.ToString(CultureInfo.InvariantCulture)} item lines."));
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
        Fields.CheckDuplicates(root, line, problems, string.Empty);
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
        if (dataset is { Length: 0 })
        {
            problems.Add(new DatasetProblem(line, "Member 'dataset' must not be empty."));
        }

        var itemCount = r.Int("itemCount", min: ResultsRules.MinItemCount);
        var provider = r.String("providerName");
        var endpointText = r.String("endpoint");
        var model = r.String("defaultModel");
        var started = r.Time("started");
        var finished = r.Time("finished");
        var parallelism = r.Int("parallelism", min: ResultsRules.MinParallelism);

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
        Fields.CheckDuplicates(root, line, problems, string.Empty);
        var r = new Fields(root, line, problems);
        var id = r.String("id", required: true);
        var itemLine = r.Int("line", min: ResultsRules.MinLine);
        var latency = r.Double("latencyMs", nonNegative: true);
        var cached = r.Bool("cached");
        var model = r.String("model");
        var inputTokens = r.Int("inputTokens", required: false, min: ResultsRules.MinTokens);
        var outputTokens = r.Int("outputTokens", required: false, min: ResultsRules.MinTokens);
        var cost = r.Double("cost", required: false, nonNegative: true);
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
        if (!element.EnumerateObject().Any())
        {
            problems.Add(new DatasetProblem(line, "Member 'answers' must not be empty."));
            return null;
        }

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
        Fields.CheckDuplicates(element, line, problems, where + ".");
        var r = new Fields(element, line, problems, where + ".");
        var kind = r.String("kind", required: true);
        var confidence = r.Double("confidence", unit: true);
        double? probability = r.Double("probability", required: false, unit: true);
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
                if (probability is null)
                {
                    problems.Add(new DatasetProblem(line, $"Member '{where}.probability' is missing: a noul answer needs it."));
                }
                else if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
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
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var level) && level >= ResultsRules.MinScoreLevel)
                {
                    record = new AnswerRecord(QuestionKind.Score, false, probability, null, level, confidence ?? 0);
                }
                else
                {
                    problems.Add(new DatasetProblem(line, $"Member '{where}.value' must be a non-negative integer."));
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
        Fields.CheckDuplicates(element, line, problems, "error.");
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

        internal static void CheckDuplicates(JsonElement element, int line, List<DatasetProblem> problems, string prefix)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    problems.Add(new DatasetProblem(line, $"Member '{prefix}{property.Name}' appears twice."));
                }
            }
        }

        internal int? Int(string name, bool required = true, int min = int.MinValue)
        {
            if (!Find(name, required, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= min)
            {
                return number;
            }

            Bad(name, min == int.MinValue ? "an integer" : $"an integer of at least {min.ToString(CultureInfo.InvariantCulture)}");
            return null;
        }

        internal double? Double(string name, bool required = true, bool unit = false, bool nonNegative = false)
        {
            if (!Find(name, required, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
                && (!unit || ResultsRules.IsUnit(number)) && (!nonNegative || ResultsRules.IsNonNegative(number)))
            {
                return number;
            }

            Bad(name, unit ? "a number from 0 to 1" : nonNegative ? "a non-negative number" : "a finite number");
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

            if (value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time) && HasOffset(value.GetString()!))
            {
                return time;
            }

            Bad(name, "an ISO 8601 timestamp with Z or an explicit offset");
            return null;
        }

        private static bool HasOffset(string text)
            => text.EndsWith('Z') || text.EndsWith('z')
                || (text.Length > 6 && (text[^6] == '+' || text[^6] == '-') && text[^3] == ':');

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
