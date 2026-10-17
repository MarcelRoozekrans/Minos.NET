using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Evaluation;

/// <summary>Reads a JSONL dataset, one item per line, collecting problems with their line numbers.</summary>
internal static class DatasetReader
{
    internal static Result<DecisionDataset, DatasetProblems> Read(ILineSource lines)
    {
        var items = new List<DatasetItem>();
        var problems = new List<DatasetProblem>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);

        var lineNumber = 0;
        while (lines.TryReadLine(out var line))
        {
            lineNumber++;
            if (line is null)
            {
                problems.Add(new DatasetProblem(lineNumber, "The line is not valid UTF-8."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Text handed to Parse(string) can hold a raw unpaired surrogate, which the JSON parser cannot transcode.
            if (!IsValidUtf16(line))
            {
                problems.Add(new DatasetProblem(lineNumber, "The line is not valid Unicode: it holds an unpaired surrogate."));
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
                continue;
            }

            using (document)
            {
                var item = ReadItem(document.RootElement, lineNumber, problems, ids);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        if (problems.Count > 0)
        {
            return Result<DecisionDataset, DatasetProblems>.Failure(new DatasetProblems([.. problems]));
        }

        return Result<DecisionDataset, DatasetProblems>.Success(new DecisionDataset([.. items]));
    }

    // The parser's message ends with "LineNumber: 0 | BytePositionInLine: N", and its line number is always 0 because
    // the parser sees one line. The byte in the line is what helps.
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

    private static bool IsValidUtf16(string text)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out _, out var used) != OperationStatus.Done)
            {
                return false;
            }

            rest = rest[used..];
        }

        return true;
    }

    private static DatasetItem? ReadItem(JsonElement root, int line, List<DatasetProblem> problems, Dictionary<string, int> ids)
    {
        var validator = new DatasetItemValidator(line, problems, ids);
        var valid = validator.Validate(root);
        if (!valid && !validator.QuestionsValid)
        {
            return null;
        }

        var builder = QuestionSet.CreateBuilder();
        var expected = new Dictionary<string, ExpectedAnswer>(StringComparer.Ordinal);

        // The questions are valid here, so the core's rules run even when other members have problems. Only a fully
        // valid line goes on to read the other members, which are not checked otherwise. Everything is read from the
        // members the validator checked: a lookup on the raw element would find the last copy of a duplicate member.
        foreach (var (key, question) in validator.Questions)
        {
            AddQuestion(builder, key, question);
            if (valid && validator.ExpectedMembers.TryGetValue(key, out var answer))
            {
                expected[key] = ReadAnswer(question["type"].GetString()!, answer);
            }
        }

        var built = builder.Build();
        if (!built.IsSuccess)
        {
            foreach (var failure in built.Error.Failures)
            {
                problems.Add(new DatasetProblem(line, CoreProblem(failure)));
            }

            if (built.Error.Failures.Count == 0)
            {
                problems.Add(new DatasetProblem(line, built.Error.Message));
            }

            return null;
        }

        if (built.Value.Warnings.Count > 0)
        {
            foreach (var warning in built.Value.Warnings)
            {
                problems.Add(new DatasetProblem(line, CoreProblem(warning)));
            }

            return null;
        }

        if (!valid)
        {
            return null;
        }

        var members = validator.ItemMembers;
        var id = members["id"].GetString()!;
        var state = ToContent(members["state"]);
        string? source = members.TryGetValue("source", out var sourceElement) ? sourceElement.GetString() : null;
        return new DatasetItem(id, state, built.Value.Definition, new ReadOnlyDictionary<string, ExpectedAnswer>(expected), source, line);
    }

    // The MIN id comes first, so a problem can be matched by its rule, then the question it is about.
    private static string CoreProblem(QuestionFailure failure)
        => failure.QuestionKey is { } key
            ? $"{failure.Rule} 'questions.{key}': {failure.Message}"
            : $"{failure.Rule}: {failure.Message}";

    // Below the question's own members, a duplicate is a problem inside 'questions', so a valid question has none and
    // the criteria can be read from the element.
    private static void AddQuestion(QuestionSetBuilder builder, string key, Dictionary<string, JsonElement> question)
    {
        var instructions = ToContent(question["instructions"]);
        question.TryGetValue("criteria", out var criteria);
        switch (question["type"].GetString())
        {
            case "noul":
                builder.Noul(key, instructions, out _, c =>
                {
                    if (criteria.ValueKind == JsonValueKind.Object)
                    {
                        if (criteria.TryGetProperty("true", out var whenTrue))
                        {
                            c.WhenTrue(ToContent(whenTrue));
                        }

                        if (criteria.TryGetProperty("false", out var whenFalse))
                        {
                            c.WhenFalse(ToContent(whenFalse));
                        }
                    }
                });
                break;
            case "choice":
                builder.Choice(key, instructions, out _, o =>
                {
                    // The validator guarantees the criteria shape; the default enumerator only covers a question with no criteria.
                    foreach (var option in criteria.ValueKind == JsonValueKind.Object ? criteria.EnumerateObject() : default)
                    {
                        if (option.Value.ValueKind == JsonValueKind.Null)
                        {
                            o.Option(option.Name);
                        }
                        else
                        {
                            o.Option(option.Name, ToCriterion(option.Value));
                        }
                    }
                });
                break;
            case "score":
                builder.Score(key, instructions, out _, l =>
                {
                    // The validator guarantees the criteria shape; the default enumerator only covers a question with no criteria.
                    foreach (var level in criteria.ValueKind == JsonValueKind.Array ? criteria.EnumerateArray() : default)
                    {
                        l.Level(ToCriterion(level));
                    }
                });
                break;
            default:
                throw new InvalidOperationException("The validator lets only noul, choice and score through.");
        }
    }

    private static ExpectedAnswer ReadAnswer(string type, JsonElement answer) => type switch
    {
        "noul" => ExpectedAnswer.ForNoul(answer.GetBoolean()),
        "choice" => ExpectedAnswer.ForChoice(answer.GetString()!),
        "score" => ExpectedAnswer.ForScore(answer.GetInt32()),
        _ => throw new InvalidOperationException("The validator lets only noul, choice and score through."),
    };

    private static Criterion ToCriterion(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Criterion.Text(element.GetString()!);
        }

        var criterion = Criterion.Text(element.GetProperty("description").GetString()!);
        if (element.TryGetProperty("examples", out var examples))
        {
            criterion = criterion.WithExamples(ToStrings(examples));
        }

        if (element.TryGetProperty("not_for", out var notFor))
        {
            criterion = criterion.WithNotFor(ToStrings(notFor));
        }

        return criterion;
    }

    private static string[] ToStrings(JsonElement array)
    {
        var values = new string[array.GetArrayLength()];
        var i = 0;
        foreach (var value in array.EnumerateArray())
        {
            values[i++] = value.GetString()!;
        }

        return values;
    }

    private static DecisionContent ToContent(JsonElement element)
        => element.ValueKind == JsonValueKind.String
            ? DecisionContent.FromString(element.GetString()!)
            : DecisionContent.FromJson(element);
}
