using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Minos.Recording.RecordFormat;

// Turns a deserialized line into a record or an outcome. A line that cannot be turned into one is a problem with a
// fixed message: it never quotes the line, which may hold state, or a decision id.
internal static class RecordLineConverter
{
    internal static bool TryToRecord(LineDto dto, int version, [NotNullWhen(true)] out DecisionRecord? record, out string message)
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

        if (!TryToErrorKind(dto.ErrorKind, out var errorKind))
        {
            message = "The record line's 'errorKind' is an error kind this reader does not know.";
            return false;
        }

        ShadowResult? shadow = null;
        if (dto.Shadow is { } shadowDto && !TryToShadow(shadowDto, out shadow, out message))
        {
            return false;
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

        // A path is all or nothing: calls, and on each answer who answered and why. Only a version 3 line has one.
        var hasPath = dto.Calls is not null;
        if (hasPath && (version < RecordLine.VersionWithPath || errorKind is not null))
        {
            message = "The record line has a path it cannot have.";
            return false;
        }

        if (!TryToCalls(dto.Calls, version, out var calls))
        {
            message = "The record line has a path that is not valid.";
            return false;
        }

        // Version 4 is written only for a path with a call that did not answer, so any other version 4 line has two encodings.
        if (version == RecordLine.VersionWithCallStatus && (calls is null || calls.All(c => c.Status == CallStatus.Answered)))
        {
            message = "The record line is version 4 without a call that did not answer.";
            return false;
        }

        if (!TryToAnswers(dto.Answers, hasPath, out var answers, out message))
        {
            return false;
        }

        // The checks above keep the constructor from throwing; if they ever drift apart, the line is a problem, not an exception.
        try
        {
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
                ToUsage(dto.Usage),
                dto.Cached,
                definition,
                state,
                dto.ImageCount,
                DecisionRecord.Freeze(dto.ImageHashes),
                answers,
                errorKind,
                DecisionRecord.Freeze(dto.RefusedKeys),
                shadow,
                calls);
        }
        catch (ArgumentException)
        {
            message = "The record line is not valid.";
            return false;
        }

        return true;
    }

    // Absent calls are no path and fine; present calls must be a non-empty list of whole calls.
    private static bool TryToCalls(List<CallDto>? dtos, int version, out IReadOnlyList<DecisionCall>? calls)
    {
        calls = null;
        if (dtos is null)
        {
            return true;
        }

        if (dtos.Count == 0)
        {
            return false;
        }

        var list = new List<DecisionCall>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (dto is null
                || string.IsNullOrEmpty(dto.Client)
                || dto.Cached is not { } cached
                || dto.Usage is { InputTokens: < 0 } or { OutputTokens: < 0 } or { Cost: < 0 })
            {
                return false;
            }

            if (!TryToCallStatus(dto, version, out var status, out var callErrorKind))
            {
                return false;
            }

            list.Add(new DecisionCall(dto.Client, dto.Model, ToUsage(dto.Usage), cached) { Status = status, ErrorKind = callErrorKind });
        }

        calls = DecisionRecord.Freeze(list);
        return true;
    }

    // A call without a status answered. Only a version 4 call has one, only Failed or NotCalledNoTimeLeft, because the writer
    // never writes Answered; an error kind goes with Failed alone. Anything else is not a call this reader knows.
    private static bool TryToCallStatus(CallDto dto, int version, out CallStatus status, out DecisionErrorKind? errorKind)
    {
        status = CallStatus.Answered;
        errorKind = null;
        if (dto.Status is not { } number)
        {
            return dto.ErrorKind is null;
        }

        // A Failed call may have no error kind: a stage can fail a call without classifying the error, so this is accepted.
        if (version < RecordLine.VersionWithCallStatus
            || number is not ((int)CallStatus.Failed or (int)CallStatus.NotCalledNoTimeLeft)
            || !TryToErrorKind(dto.ErrorKind, out errorKind)
            || (errorKind is not null && number != (int)CallStatus.Failed))
        {
            return false;
        }

        status = (CallStatus)number;
        return true;
    }

    private static bool TryToShadow(ShadowDto dto, [NotNullWhen(true)] out ShadowResult? shadow, out string message)
    {
        shadow = null;
        message = string.Empty;
        if (string.IsNullOrEmpty(dto.Provider))
        {
            message = "The record line's 'shadow' needs a 'provider'.";
            return false;
        }

        if (!TryToErrorKind(dto.ErrorKind, out var errorKind))
        {
            message = "The record line's 'shadow' has an 'errorKind' this reader does not know.";
            return false;
        }

        if (!TryToAnswers(dto.Answers, withPath: false, out var answers, out message))
        {
            return false;
        }

        shadow = new ShadowResult(
            dto.Provider,
            dto.Endpoint,
            dto.DefaultModel,
            dto.RequestedModel,
            dto.ServedModel,
            dto.ResponseId,
            dto.UpstreamProvider,
            TimeSpan.FromTicks((long)Math.Round(dto.LatencyMs * TimeSpan.TicksPerMillisecond)),
            ToUsage(dto.Usage),
            answers,
            errorKind,
            DecisionRecord.Freeze(dto.RefusedKeys));
        return true;
    }

    // An absent error kind is fine and gives null; a name that is not a defined kind fails.
    private static bool TryToErrorKind(string? name, out DecisionErrorKind? errorKind)
    {
        errorKind = null;
        if (name is null)
        {
            return true;
        }

        if (!Enum.TryParse<DecisionErrorKind>(name, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        errorKind = parsed;
        return true;
    }

    private static bool TryToAnswers(List<AnswerDto>? dtos, bool withPath, out IReadOnlyList<RecordedAnswer> answers, out string message)
    {
        answers = [];
        message = string.Empty;
        var list = new List<RecordedAnswer>(dtos?.Count ?? 0);
        foreach (var answer in dtos ?? [])
        {
            if (answer.Kind is not (null or "noul" or "choice" or "score"))
            {
                message = "The record line has an answer of a kind this reader does not know.";
                return false;
            }

            if (!TryToAnswer(answer, withPath, out var recorded))
            {
                message = "The record line has an answer that is not valid.";
                return false;
            }

            list.Add(recorded);
        }

        answers = DecisionRecord.Freeze(list);
        return true;
    }

    private static DecisionUsage? ToUsage(UsageDto? u)
        => u is null ? null : new DecisionUsage { InputTokens = u.InputTokens, OutputTokens = u.OutputTokens, Cost = u.Cost };

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

    private static bool TryToAnswer(AnswerDto dto, bool withPath, out RecordedAnswer answer)
    {
        answer = default;
        if (!TryToRoute(dto, withPath, out var route))
        {
            return false;
        }

        if (string.IsNullOrEmpty(dto.Key))
        {
            return false;
        }

        ProbabilitySource source;
        switch (dto.ProbabilitySource)
        {
            case null:
                source = ProbabilitySource.Model;
                break;
            case "logprobs":
                source = ProbabilitySource.Logprobs;
                break;
            case "verbalized":
                source = ProbabilitySource.Verbalized;
                break;
            default:
                return false;
        }

        var probabilities = DecisionRecord.Freeze(dto.Probabilities);
        switch (dto.Kind)
        {
            case "noul":
                answer = new RecordedAnswer(dto.Key, QuestionKind.Noul, -1, dto.Probability, 0, dto.Confidence, probabilities, source, dto.AnsweredBy, route, dto.PrimaryCertainty, dto.TargetCertainty);
                return true;
            case "choice" when dto.ChosenIndex is { } index:
                answer = new RecordedAnswer(dto.Key, QuestionKind.Choice, index, 0, 0, dto.Confidence, probabilities, source, dto.AnsweredBy, route, dto.PrimaryCertainty, dto.TargetCertainty);
                return true;
            case "score" when dto.ChosenIndex is { } level:
                answer = new RecordedAnswer(dto.Key, QuestionKind.Score, level, 0, dto.Expected, dto.Confidence, probabilities, source, dto.AnsweredBy, route, dto.PrimaryCertainty, dto.TargetCertainty);
                return true;
            default:
                return false;
        }
    }

    // On a path, an answer needs who answered, why and the primary's certainty; off a path it has none of the four.
    private static bool TryToRoute(AnswerDto dto, bool withPath, out RouteReason? route)
    {
        route = null;
        if (!withPath)
        {
            return dto.AnsweredBy is null && dto.Reason is null && dto.PrimaryCertainty is null && dto.TargetCertainty is null;
        }

        if (string.IsNullOrEmpty(dto.AnsweredBy)
            || dto.Reason is null
            || !Enum.TryParse<RouteReason>(dto.Reason, ignoreCase: false, out var reason)
            || !Enum.IsDefined(reason)
            || dto.PrimaryCertainty is not { } primary
            || !IsCertainty(primary)
            || dto.TargetCertainty is { } target && !IsCertainty(target))
        {
            return false;
        }

        route = reason;
        return true;
    }

    private static bool IsCertainty(double value) => value is >= 0 and <= 1;
}
