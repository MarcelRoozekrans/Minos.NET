using System.Text.Json;

namespace Minos.Recording.RecordFormat;

// The "questions" member of a record line: the dataset line's shape, as docs/datasets.md describes it. A question is
// { "type": "noul" | "choice" | "score", "instructions": text-or-JSON, "criteria": ... }. It is written and read here,
// and not through Minos.NET.Evaluation, which the package does not reference.
internal static class QuestionSetFormat
{
    internal static void Write(Utf8JsonWriter w, QuestionSetDefinition definition)
    {
        w.WriteStartObject();
        foreach (var question in definition.Questions)
        {
            w.WriteStartObject(question.Key);
            w.WriteString("type", TypeName(question.Kind));
            w.WritePropertyName("instructions");
            WriteContent(w, question.Instructions);
            switch (question.Kind)
            {
                case QuestionKind.Noul:
                    WriteNoulCriteria(w, question);
                    break;
                case QuestionKind.Choice:
                    w.WriteStartObject("criteria");
                    foreach (var option in question.Options)
                    {
                        w.WritePropertyName(option.Key);
                        if (option.Criterion is null)
                        {
                            w.WriteNullValue();
                        }
                        else
                        {
                            WriteCriterion(w, option.Criterion);
                        }
                    }

                    w.WriteEndObject();
                    break;
                default:
                    w.WriteStartArray("criteria");
                    foreach (var level in question.Options)
                    {
                        WriteCriterion(w, level.Criterion!);
                    }

                    w.WriteEndArray();
                    break;
            }

            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    internal static void WriteContent(Utf8JsonWriter w, DecisionContent content)
    {
        if (content.TryGetString(out var text))
        {
            w.WriteStringValue(text);
        }
        else if (content.TryGetJson(out var json))
        {
            json.WriteTo(w);
        }
        else
        {
            throw new InvalidOperationException("The content is uninitialized.");
        }
    }

    internal static DecisionContent ReadContent(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => DecisionContent.FromString(element.GetString()!),
        JsonValueKind.Object or JsonValueKind.Array => DecisionContent.FromJson(element),
        _ => throw new FormatException("The content must be a string, object or array."),
    };

    // Throws FormatException, ArgumentException or InvalidOperationException for a set that the core would not build.
    internal static QuestionSetDefinition Read(JsonElement questions)
    {
        if (questions.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("'questions' must be an object.");
        }

        var list = new List<QuestionDefinition>();
        foreach (var property in questions.EnumerateObject())
        {
            list.Add(ReadQuestion(property.Name, property.Value));
        }

        if (list.Count == 0)
        {
            throw new FormatException("'questions' holds no question.");
        }

        return new QuestionSetDefinition([.. list]);
    }

    private static QuestionDefinition ReadQuestion(string key, JsonElement question)
    {
        if (question.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A question must be an object.");
        }

        if (!question.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("A question needs a 'type'.");
        }

        if (!question.TryGetProperty("instructions", out var instructionsElement))
        {
            throw new FormatException("A question needs 'instructions'.");
        }

        var instructions = ReadContent(instructionsElement);
        question.TryGetProperty("criteria", out var criteria);
        switch (type.GetString())
        {
            case "noul":
                return ReadNoul(key, instructions, criteria);
            case "choice":
                if (criteria.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("A Choice question needs object criteria.");
                }

                var options = new List<OptionDefinition>();
                foreach (var option in criteria.EnumerateObject())
                {
                    options.Add(new OptionDefinition(option.Name, option.Value.ValueKind == JsonValueKind.Null ? null : ReadCriterion(option.Value)));
                }

                return QuestionDefinition.Choice(key, instructions, [.. options]);
            case "score":
                if (criteria.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("A Score question needs array criteria.");
                }

                var levels = new List<Criterion>();
                foreach (var level in criteria.EnumerateArray())
                {
                    levels.Add(ReadCriterion(level));
                }

                return QuestionDefinition.Score(key, instructions, [.. levels]);
            default:
                throw new FormatException("A question's 'type' must be noul, choice or score.");
        }
    }

    private static QuestionDefinition ReadNoul(string key, DecisionContent instructions, JsonElement criteria)
    {
        DecisionContent? whenTrue = null;
        DecisionContent? whenFalse = null;
        if (criteria.ValueKind == JsonValueKind.Object)
        {
            if (criteria.TryGetProperty("true", out var t))
            {
                whenTrue = ReadContent(t);
            }

            if (criteria.TryGetProperty("false", out var f))
            {
                whenFalse = ReadContent(f);
            }
        }
        else if (criteria.ValueKind != JsonValueKind.Undefined)
        {
            throw new FormatException("A Noul question's criteria must be an object.");
        }

        return QuestionDefinition.Noul(key, instructions, whenTrue, whenFalse);
    }

    private static void WriteNoulCriteria(Utf8JsonWriter w, QuestionDefinition question)
    {
        if (question.WhenTrue is null && question.WhenFalse is null)
        {
            return;
        }

        w.WriteStartObject("criteria");
        if (question.WhenTrue is { } whenTrue)
        {
            w.WritePropertyName("true");
            WriteContent(w, whenTrue);
        }

        if (question.WhenFalse is { } whenFalse)
        {
            w.WritePropertyName("false");
            WriteContent(w, whenFalse);
        }

        w.WriteEndObject();
    }

    // A text criterion with no examples is its description; with examples it is { description, examples, not_for }.
    // A JSON criterion, which the dataset format does not have in version 1, is written as its JSON.
    private static void WriteCriterion(Utf8JsonWriter w, Criterion criterion)
    {
        if (criterion.Description is not { } description)
        {
            WriteContent(w, criterion.JsonContent!.Value);
            return;
        }

        if (criterion.Examples.Count == 0 && criterion.NotFor.Count == 0)
        {
            w.WriteStringValue(description);
            return;
        }

        w.WriteStartObject();
        w.WriteString("description", description);
        WriteTexts(w, "examples", criterion.Examples);
        WriteTexts(w, "not_for", criterion.NotFor);
        w.WriteEndObject();
    }

    private static void WriteTexts(Utf8JsonWriter w, string name, IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
        {
            return;
        }

        w.WriteStartArray(name);
        foreach (var text in texts)
        {
            w.WriteStringValue(text);
        }

        w.WriteEndArray();
    }

    private static Criterion ReadCriterion(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Criterion.Text(element.GetString()!);
        }

        if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new FormatException("A criterion must be a string, object or array.");
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("description", out var description)
            && description.ValueKind == JsonValueKind.String)
        {
            var criterion = Criterion.Text(description.GetString()!);
            if (element.TryGetProperty("examples", out var examples))
            {
                criterion = criterion.WithExamples(ReadTexts(examples));
            }

            if (element.TryGetProperty("not_for", out var notFor))
            {
                criterion = criterion.WithNotFor(ReadTexts(notFor));
            }

            return criterion;
        }

        return Criterion.Json(DecisionContent.FromJson(element));
    }

    private static string[] ReadTexts(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("'examples' and 'not_for' must be arrays.");
        }

        var texts = new string[array.GetArrayLength()];
        var i = 0;
        foreach (var value in array.EnumerateArray())
        {
            texts[i++] = value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new FormatException("'examples' and 'not_for' hold strings.");
        }

        return texts;
    }

    private static string TypeName(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "noul",
        QuestionKind.Choice => "choice",
        _ => "score",
    };
}
