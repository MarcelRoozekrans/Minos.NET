using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Minos.Recording.RecordFormat;

// Turns a deserialized line into a record or an outcome. A line that cannot be turned into one is a problem with a
// fixed message: it never quotes the line, which may hold state, or a decision id.
internal static class RecordLineConverter
{
    internal static bool TryToRecord(LineDto dto, [NotNullWhen(true)] out DecisionRecord? record, out string message)
    {
        record = null;
        message = string.Empty;
        if (string.IsNullOrEmpty(dto.DecisionId) || string.IsNullOrEmpty(dto.Provider) || dto.Started is not { } started)
        {
            message = "The record line needs a 'decisionId', a 'provider' and a 'started'.";
            return false;
        }

        if (dto.State.ValueKind == JsonValueKind.Undefined || dto.Questions.ValueKind == JsonValueKind.Undefined)
        {
            message = "The record line needs 'questions' and 'state'.";
            return false;
        }

        DecisionErrorKind? errorKind = null;
        if (dto.ErrorKind is { } errorName)
        {
            if (!Enum.TryParse<DecisionErrorKind>(errorName, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
            {
                message = "The record line's 'errorKind' is an error kind this reader does not know.";
                return false;
            }

            errorKind = parsed;
        }

        QuestionSetDefinition definition;
        DecisionContent state;
        try
        {
            definition = QuestionSetFormat.Read(dto.Questions);
            state = QuestionSetFormat.ReadContent(dto.State);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            message = "The record line's 'questions' or 'state' is not valid.";
            return false;
        }

        var answers = new List<RecordedAnswer>(dto.Answers?.Count ?? 0);
        foreach (var answer in dto.Answers ?? [])
        {
            if (answer.Kind is not (null or "noul" or "choice" or "score"))
            {
                message = "The record line has an answer of a kind this reader does not know.";
                return false;
            }

            if (!TryToAnswer(answer, out var recorded))
            {
                message = "The record line has an answer that is not valid.";
                return false;
            }

            answers.Add(recorded);
        }

        var usage = dto.Usage is { } u ? new DecisionUsage { InputTokens = u.InputTokens, OutputTokens = u.OutputTokens, Cost = u.Cost } : null;
        record = new DecisionRecord(
            dto.DecisionId,
            dto.ErasureKey,
            started,
            TimeSpan.FromTicks((long)Math.Round(dto.LatencyMs * TimeSpan.TicksPerMillisecond)),
            dto.Provider,
            dto.Endpoint,
            dto.DefaultModel,
            dto.RequestedModel,
            dto.ServedModel,
            dto.ResponseId,
            dto.UpstreamProvider,
            usage,
            dto.Cached,
            definition,
            state,
            dto.ImageCount,
            DecisionRecord.Freeze(dto.ImageHashes),
            DecisionRecord.Freeze(answers),
            errorKind,
            DecisionRecord.Freeze(dto.RefusedKeys));
        return true;
    }

    internal static bool TryToOutcome(LineDto dto, [NotNullWhen(true)] out DecisionOutcome? outcome, out string message)
    {
        try
        {
            return TryToOutcomeCore(dto, out outcome, out message);
        }
        catch (InvalidOperationException)
        {
            // A key or a value written as an escape that is not text, such as a lone surrogate, cannot be read as a string.
            outcome = null;
            message = "The outcome line is not valid.";
            return false;
        }
    }

    private static bool TryToOutcomeCore(LineDto dto, out DecisionOutcome? outcome, out string message)
    {
        outcome = null;
        message = "The outcome line is not valid.";
        if (string.IsNullOrEmpty(dto.Source) || dto.At is not { } at)
        {
            message = "The outcome line needs a 'source' and an 'at'.";
            return false;
        }

        switch (dto.Type)
        {
            case "confirmed":
                if (dto.Confirmed.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var answers = new Dictionary<string, ConfirmedAnswer>(StringComparer.Ordinal);
                foreach (var property in dto.Confirmed.EnumerateObject())
                {
                    if (property.Name.Length == 0 || answers.ContainsKey(property.Name) || !TryToConfirmed(property.Value, out var answer))
                    {
                        return false;
                    }

                    answers[property.Name] = answer;
                }

                if (answers.Count == 0)
                {
                    return false;
                }

                outcome = new ConfirmedOutcome(dto.Source, at, answers);
                return true;
            case "signal":
                if (string.IsNullOrEmpty(dto.Name) || dto.Value is { } value && !double.IsFinite(value))
                {
                    return false;
                }

                outcome = new SignalOutcome(dto.Source, at, dto.Name, dto.Value, dto.Note);
                return true;
            default:
                message = "The outcome line's 'type' must be confirmed or signal.";
                return false;
        }
    }

    private static bool TryToConfirmed(JsonElement value, out ConfirmedAnswer answer)
    {
        answer = default;
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                answer = ConfirmedAnswer.ForNoul(value.GetBoolean());
                return true;
            case JsonValueKind.String when value.GetString() is { Length: > 0 } key:
                answer = ConfirmedAnswer.ForChoice(key);
                return true;
            case JsonValueKind.Number when value.TryGetInt32(out var level) && level >= 0:
                answer = ConfirmedAnswer.ForScore(level);
                return true;
            default:
                return false;
        }
    }

    private static bool TryToAnswer(AnswerDto dto, out RecordedAnswer answer)
    {
        answer = default;
        if (string.IsNullOrEmpty(dto.Key))
        {
            return false;
        }

        var probabilities = DecisionRecord.Freeze(dto.Probabilities);
        switch (dto.Kind)
        {
            case "noul":
                answer = new RecordedAnswer(dto.Key, QuestionKind.Noul, -1, dto.Probability, 0, dto.Confidence, probabilities);
                return true;
            case "choice" when dto.ChosenIndex is { } index:
                answer = new RecordedAnswer(dto.Key, QuestionKind.Choice, index, 0, 0, dto.Confidence, probabilities);
                return true;
            case "score" when dto.ChosenIndex is { } level:
                answer = new RecordedAnswer(dto.Key, QuestionKind.Score, level, 0, dto.Expected, dto.Confidence, probabilities);
                return true;
            default:
                return false;
        }
    }
}
