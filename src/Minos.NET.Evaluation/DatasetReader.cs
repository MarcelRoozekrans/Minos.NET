using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Evaluation;

/// <summary>Reads a JSONL dataset, one item per line, collecting problems with their line numbers.</summary>
internal static class DatasetReader
{
    internal static Result<DecisionDataset, DatasetProblems> Read(TextReader reader)
    {
        var items = new List<DatasetItem>();
        var problems = new List<DatasetProblem>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);

        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
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
                problems.Add(new DatasetProblem(lineNumber, $"The line is not valid JSON: {ex.Message}"));
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
        // valid line goes on to read the other members, which are not checked otherwise.
        foreach (var question in root.GetProperty("questions").EnumerateObject())
        {
            AddQuestion(builder, question.Name, question.Value);
            if (valid && root.GetProperty("expected").TryGetProperty(question.Name, out var answer))
            {
                expected[question.Name] = ReadAnswer(question.Value.GetProperty("type").GetString()!, answer);
            }
        }

        var built = builder.Build();
        if (!built.IsSuccess)
        {
            foreach (var failure in built.Error.Failures)
            {
                problems.Add(new DatasetProblem(line, $"{failure.Rule}: {failure.Message}"));
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
                problems.Add(new DatasetProblem(line, $"{warning.Rule}: {warning.Message}"));
            }

            return null;
        }

        if (!valid)
        {
            return null;
        }

        var id = root.GetProperty("id").GetString()!;
        var state = ToContent(root.GetProperty("state"));
        string? source = root.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() : null;
        return new DatasetItem(id, state, built.Value.Definition, expected, source, line);
    }

    private static void AddQuestion(QuestionSetBuilder builder, string key, JsonElement question)
    {
        var instructions = ToContent(question.GetProperty("instructions"));
        question.TryGetProperty("criteria", out var criteria);
        switch (question.GetProperty("type").GetString())
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
