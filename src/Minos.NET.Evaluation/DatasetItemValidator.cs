using System.Globalization;
using System.Text.Json;

namespace Minos.Evaluation;

/// <summary>
/// Checks the shape of one dataset line against format version 1 before anything is built from it, so that no
/// content can make the reader throw. Every problem names the member and its path.
/// </summary>
internal sealed class DatasetItemValidator
{
    private static readonly string[] ItemMembers = ["id", "state", "questions", "expected", "source", "version"];
    private static readonly string[] QuestionMembers = ["type", "instructions", "criteria"];
    private static readonly string[] CriterionMembers = ["description", "examples", "not_for"];
    private static readonly string[] NoulCriteriaMembers = ["true", "false"];

    private readonly int _line;
    private readonly List<DatasetProblem> _problems;
    private readonly Dictionary<string, int> _ids;

    internal DatasetItemValidator(int line, List<DatasetProblem> problems, Dictionary<string, int> ids)
    {
        _line = line;
        _problems = problems;
        _ids = ids;
    }

    /// <summary>
    /// Gets a value saying whether the <c>questions</c> object is sound enough to build a question set from, even when
    /// other members of the line have problems. Set by <see cref="Validate"/>.
    /// </summary>
    internal bool QuestionsValid { get; private set; }

    /// <summary>Validates the line and reports whether it had no problem.</summary>
    internal bool Validate(JsonElement root)
    {
        var before = _problems.Count;
        if (root.ValueKind != JsonValueKind.Object)
        {
            Add("The line must be a JSON object.");
            return false;
        }

        // A JSON string can hold an escaped unpaired surrogate. The parser accepts it but reading it as text throws,
        // so every string and member name is checked once here and the checks below can read them safely.
        CheckText(root, string.Empty);
        if (_problems.Count > before)
        {
            return false;
        }

        var members = Members(root, string.Empty, ItemMembers);
        CheckId(members);
        CheckVersion(members);

        if (!members.TryGetValue("state", out var state))
        {
            Missing("state");
        }
        else if (!IsContent(state))
        {
            Add("'state' must be a JSON string, object or array.");
        }

        if (members.TryGetValue("source", out var source) && source.ValueKind != JsonValueKind.String)
        {
            Add("'source' must be a string.");
        }

        var shapes = new Dictionary<string, QuestionShape>(StringComparer.Ordinal);
        var questionsKnown = false;
        var beforeQuestions = _problems.Count;
        if (!members.TryGetValue("questions", out var questions))
        {
            Missing("questions");
        }
        else if (questions.ValueKind != JsonValueKind.Object)
        {
            Add("'questions' must be a JSON object.");
        }
        else
        {
            questionsKnown = true;
            foreach (var (key, question) in Members(questions, "questions", null))
            {
                shapes[key] = ValidateQuestion($"questions.{key}", question);
            }
        }

        QuestionsValid = questionsKnown && _problems.Count == beforeQuestions;

        if (!members.TryGetValue("expected", out var expected))
        {
            Missing("expected");
        }
        else if (expected.ValueKind != JsonValueKind.Object)
        {
            Add("'expected' must be a JSON object.");
        }
        else
        {
            foreach (var (key, answer) in Members(expected, "expected", null))
            {
                if (!shapes.TryGetValue(key, out var shape))
                {
                    if (questionsKnown)
                    {
                        Add($"'expected.{key}' has no question named '{key}' in 'questions'.");
                    }
                }
                else if (shape.Type is not null)
                {
                    ValidateAnswer($"expected.{key}", shape, answer);
                }
            }
        }

        return _problems.Count == before;
    }

    private void CheckId(Dictionary<string, JsonElement> members)
    {
        if (!members.TryGetValue("id", out var id))
        {
            Missing("id");
        }
        else if (id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 } text)
        {
            Add("'id' must be a non-empty string.");
        }
        else if (!_ids.TryAdd(text, _line))
        {
            Add($"'id' is '{text}', which is already used on line {_ids[text].ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private void CheckVersion(Dictionary<string, JsonElement> members)
    {
        if (members.TryGetValue("version", out var version) && !(TryInteger(version, out var number) && number == 1))
        {
            Add(IsWholeNumberText(version)
                ? $"This file uses dataset format version {version.GetRawText()}; this loader reads version 1."
                : "'version' must be the number 1.");
        }
    }

    private QuestionShape ValidateQuestion(string path, JsonElement question)
    {
        if (question.ValueKind != JsonValueKind.Object)
        {
            Add($"'{path}' must be a JSON object.");
            return default;
        }

        var members = Members(question, path, QuestionMembers);
        string? type = null;
        if (!members.TryGetValue("type", out var typeElement))
        {
            Missing($"{path}.type");
        }
        else if (typeElement.ValueKind != JsonValueKind.String)
        {
            Add($"'{path}.type' must be a string.");
        }
        else if (typeElement.GetString() is "noul" or "choice" or "score")
        {
            type = typeElement.GetString();
        }
        else
        {
            Add($"'{path}.type' is '{typeElement.GetString()}'; it must be 'noul', 'choice' or 'score'.");
        }

        if (!members.TryGetValue("instructions", out var instructions))
        {
            Missing($"{path}.instructions");
        }
        else if (!IsContent(instructions))
        {
            Add($"'{path}.instructions' must be a JSON string, object or array.");
        }

        members.TryGetValue("criteria", out var criteria);
        var criteriaPath = $"{path}.criteria";
        var present = members.ContainsKey("criteria");
        switch (type)
        {
            case "noul":
                if (present)
                {
                    ValidateNoulCriteria(criteriaPath, criteria);
                }

                return new QuestionShape(type, null, 0);
            case "choice":
                return new QuestionShape(type, present ? ValidateChoiceCriteria(criteriaPath, criteria) : null, 0);
            case "score":
                return new QuestionShape(type, null, present ? ValidateScoreCriteria(criteriaPath, criteria) : -1);
            default:
                return default;
        }
    }

    private void ValidateNoulCriteria(string path, JsonElement criteria)
    {
        if (criteria.ValueKind != JsonValueKind.Object)
        {
            Add($"'{path}' must be a JSON object with 'true' and 'false'.");
            return;
        }

        foreach (var (name, value) in Members(criteria, path, NoulCriteriaMembers))
        {
            if (!IsContent(value))
            {
                Add($"'{path}.{name}' must be a JSON string, object or array.");
            }
        }
    }

    private HashSet<string>? ValidateChoiceCriteria(string path, JsonElement criteria)
    {
        if (criteria.ValueKind != JsonValueKind.Object)
        {
            Add($"'{path}' must be a JSON object keyed by option.");
            return null;
        }

        var options = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in Members(criteria, path, null))
        {
            options.Add(name);
            if (value.ValueKind != JsonValueKind.Null)
            {
                ValidateCriterion($"{path}.{name}", value);
            }
        }

        return options;
    }

    private int ValidateScoreCriteria(string path, JsonElement criteria)
    {
        if (criteria.ValueKind != JsonValueKind.Array)
        {
            Add($"'{path}' must be a JSON array of levels.");
            return -1;
        }

        var index = 0;
        foreach (var level in criteria.EnumerateArray())
        {
            ValidateCriterion($"{path}[{index.ToString(CultureInfo.InvariantCulture)}]", level);
            index++;
        }

        return index;
    }

    private void ValidateCriterion(string path, JsonElement criterion)
    {
        if (criterion.ValueKind == JsonValueKind.String)
        {
            return;
        }

        if (criterion.ValueKind != JsonValueKind.Object)
        {
            Add($"'{path}' must be a string or an object with 'description'.");
            return;
        }

        var members = Members(criterion, path, CriterionMembers);
        if (!members.TryGetValue("description", out var description))
        {
            Missing($"{path}.description");
        }
        else if (description.ValueKind != JsonValueKind.String)
        {
            Add($"'{path}.description' must be a string.");
        }

        foreach (var name in (ReadOnlySpan<string>)["examples", "not_for"])
        {
            if (members.TryGetValue(name, out var list))
            {
                ValidateStrings($"{path}.{name}", list);
            }
        }
    }

    private void ValidateStrings(string path, JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            Add($"'{path}' must be an array of strings.");
            return;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                Add($"'{path}' must be an array of strings.");
                return;
            }
        }
    }

    private void ValidateAnswer(string path, QuestionShape shape, JsonElement answer)
    {
        switch (shape.Type)
        {
            case "noul":
                if (answer.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    Add($"'{path}' must be true or false for a noul question.");
                }

                break;
            case "choice":
                if (answer.ValueKind != JsonValueKind.String)
                {
                    Add($"'{path}' must be an option key string for a choice question.");
                }
                else if (shape.Options is { } options && !options.Contains(answer.GetString()!))
                {
                    Add($"'{path}' is '{answer.GetString()}', which is not an option in the question's criteria; option keys are case-sensitive.");
                }

                break;
            default:
                if (!IsIntegerNumber(answer))
                {
                    Add($"'{path}' must be an integer level index from 0 for a score question.");
                }
                else if (!TryInteger(answer, out var level) || level < 0 || (shape.Levels >= 0 && level >= shape.Levels))
                {
                    Add($"'{path}' is {answer.GetRawText()}, which is out of range for the question's levels.");
                }

                break;
        }
    }

    private Dictionary<string, JsonElement> Members(JsonElement element, string path, string[]? allowed)
    {
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var shown = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            if (allowed is not null && Array.IndexOf(allowed, property.Name) < 0)
            {
                Add($"Unknown member '{shown}'.");
            }
            else if (!members.TryAdd(property.Name, property.Value))
            {
                Add($"Duplicate member '{shown}'.");
            }
        }

        return members;
    }

    // A number written without a fraction or exponent, such as 2. Those are the versions a later loader could name.
    private static bool IsWholeNumberText(JsonElement element)
        => element.ValueKind == JsonValueKind.Number
            && element.GetRawText().AsSpan().TrimStart('-').IndexOfAnyExceptInRange('0', '9') < 0;

    private static bool TryInteger(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number
            && int.TryParse(element.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsIntegerNumber(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText().AsSpan().TrimStart('-');
        return raw.Length > 0 && raw.IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private void CheckText(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (!IsValidText(element))
                {
                    Add($"'{path}' holds text that is not valid Unicode, such as an unpaired surrogate escape.");
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    string name;
                    try
                    {
                        name = property.Name;
                    }
                    catch (InvalidOperationException)
                    {
                        Add($"A member name in '{path}' is not valid Unicode, such as an unpaired surrogate escape.");
                        continue;
                    }

                    CheckText(property.Value, path.Length == 0 ? name : $"{path}.{name}");
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var entry in element.EnumerateArray())
                {
                    CheckText(entry, $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]");
                    index++;
                }

                break;
            default:
                break;
        }
    }

    private static bool IsValidText(JsonElement element)
    {
        try
        {
            _ = element.GetString();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsContent(JsonElement element)
        => element.ValueKind is JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array;

    private void Missing(string path) => Add($"Missing member '{path}'.");

    private void Add(string message) => _problems.Add(new DatasetProblem(_line, message));

    // Type is null when the question's type was not usable; Options is null when a choice has no usable criteria;
    // Levels is -1 when a score has no usable criteria.
    private readonly record struct QuestionShape(string? Type, HashSet<string>? Options, int Levels);
}
