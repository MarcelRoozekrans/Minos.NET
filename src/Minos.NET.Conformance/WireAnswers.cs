using System.Text.Json;

namespace Minos.Conformance;

/// <summary>One answer as the response sent it: the question key it names, its <c>type</c> and its JSON.</summary>
/// <param name="Key">The question key: the property name on System One, <c>name</c> on Decisions.</param>
/// <param name="Type">The <c>type</c>, or <see langword="null"/> when it is missing or not a string.</param>
/// <param name="Body">The answer's JSON.</param>
internal sealed record WireAnswer(string Key, string? Type, JsonElement Body)
{
    /// <summary>Gets a property, when the answer is an object and has it.</summary>
    public bool TryGet(string property, out JsonElement value)
    {
        if (Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(property, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets a string property, or <see langword="null"/>.</summary>
    public string? Text(string property) => TryGet(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Reads the probabilities by option key: an object of numbers on System One, an array of <c>value</c>,
    /// <c>label</c> and <c>probability</c> on Decisions, keyed by the <c>value</c>; the label is used only for a Choice entry without one. A value that is not a number
    /// is kept as <see langword="null"/>, so D1 can name it.
    /// </summary>
    /// <returns>The probabilities, or <see langword="null"/> when they are missing or not in the wire's shape.</returns>
    public List<(string Key, double? Value)>? Probabilities(ConformanceWire wire)
    {
        if (!TryGet("probabilities", out var probabilities))
        {
            return null;
        }

        var read = new List<(string Key, double? Value)>();
        if (wire == ConformanceWire.SystemOne)
        {
            if (probabilities.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in probabilities.EnumerateObject())
            {
                read.Add((property.Name, Number(property.Value)));
            }

            return read;
        }

        if (probabilities.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in probabilities.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // A Score level is its value, as the core reads it; the label is display text and may differ.
            var isScore = string.Equals(Type, "score", StringComparison.Ordinal);
            string? key = null;
            if (entry.TryGetProperty("value", out var value))
            {
                key = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null,
                };
            }

            if (key is null && !isScore && entry.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
            {
                key = label.GetString();
            }

            if (key is null)
            {
                return null;
            }

            read.Add((key, entry.TryGetProperty("probability", out var probability) ? Number(probability) : null));
        }

        return read;
    }

    /// <summary>Gets a number property that is finite, or <see langword="null"/>.</summary>
    public double? Number(string property) => TryGet(property, out var value) ? Number(value) : null;

    private static double? Number(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}

/// <summary>Reads a response body's answers in either wire format, without Minos, so the checks can compare the two.</summary>
internal static class WireAnswers
{
    /// <summary>Why the checks that read an answer body skip on <see cref="ConformanceWire.ChatCompletions"/>.</summary>
    public const string NoAnswerBody = "A chat completion carries no answer body; the answers are checked as Minos read them.";

    /// <summary>The <c>type</c> a question kind has on a wire: Noul is <c>noul</c> on System One and <c>predicate</c> on Decisions.</summary>
    public static string TypeOf(QuestionKind kind, ConformanceWire wire) => kind switch
    {
        QuestionKind.Noul => wire == ConformanceWire.SystemOne ? "noul" : "predicate",
        QuestionKind.Choice => "choice",
        _ => "score",
    };

    /// <summary>The property holding a Noul's probability: <c>noul</c> on System One, <c>probability</c> on Decisions.</summary>
    public static string NoulProperty(ConformanceWire wire) => wire == ConformanceWire.SystemOne ? "noul" : "probability";

    /// <summary>Reads the last exchange's body as a JSON object.</summary>
    public static bool TryBody(ProbeOutcome outcome, out JsonElement body, out string problem)
    {
        body = default;
        if (outcome.Last is not { } last)
        {
            problem = "No request was sent" + (outcome.Error is { } error ? ": " + error.Kind.ToString() + ", " + error.Message : ".");
            return false;
        }

        if (!last.TryGetJson(out body) || body.ValueKind != JsonValueKind.Object)
        {
            problem = "The response body is not a JSON object.";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    /// <summary>Reads the answers of the last exchange's body, in the order they were sent; a chat completion has none to read.</summary>
    public static bool TryRead(ProbeOutcome outcome, ConformanceWire wire, out List<WireAnswer> answers, out string problem)
    {
        answers = [];
        if (wire == ConformanceWire.ChatCompletions)
        {
            problem = NoAnswerBody;
            return false;
        }

        if (!TryBody(outcome, out var body, out problem))
        {
            return false;
        }

        if (!body.TryGetProperty("answers", out var list))
        {
            problem = "The response has no answers.";
            return false;
        }

        if (wire == ConformanceWire.SystemOne)
        {
            if (list.ValueKind != JsonValueKind.Object)
            {
                problem = "The answers are not a JSON object keyed by question.";
                return false;
            }

            foreach (var answer in list.EnumerateObject())
            {
                answers.Add(new WireAnswer(answer.Name, TypeText(answer.Value), answer.Value));
            }

            return true;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            problem = "The answers are not a JSON array.";
            return false;
        }

        foreach (var answer in list.EnumerateArray())
        {
            var name = answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("name", out var key) && key.ValueKind == JsonValueKind.String
                ? key.GetString() ?? string.Empty
                : string.Empty;
            answers.Add(new WireAnswer(name, TypeText(answer), answer));
        }

        return true;
    }

    /// <summary>Finds the one answer for a key, or <see langword="null"/>.</summary>
    public static WireAnswer? Find(List<WireAnswer> answers, string key)
    {
        foreach (var answer in answers)
        {
            if (string.Equals(answer.Key, key, StringComparison.Ordinal))
            {
                return answer;
            }
        }

        return null;
    }

    private static string? TypeText(JsonElement answer)
        => answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;
}
