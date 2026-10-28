using System.Globalization;
using System.Text.Json;

namespace Minos.Conformance;

// The checks on the chat-completions wire. A chat completion carries no answer body, so D1 to D4 and M1 read what Minos
// read, and R1, R3, E3 and E4 read every exchange of a probe, one per question.
public static partial class ConformanceChecks
{
    private static CheckResult ChatD1(ProbeOutcome outcome)
    {
        if (outcome.Response is not { } response)
        {
            return CheckResult.Skip("D1", MinosError);
        }

        var count = 0;
        var examined = 0;
        var problems = new List<string>();
        foreach (var (question, answer) in ReadAnswers(outcome.Probe, response))
        {
            examined++;
            if (question.Kind == QuestionKind.Noul)
            {
                count++;
                if (!IsUnit(answer.Probability))
                {
                    problems.Add(Invariant($"'{question.Key}' is {answer.Probability}"));
                }

                continue;
            }

            var probabilities = answer.Probabilities;
            for (var i = 0; i < probabilities.Length; i++)
            {
                count++;
                if (!IsUnit(probabilities[i]))
                {
                    problems.Add(Invariant($"'{question.Key}' gives '{OptionKey(question, i)}' {probabilities[i]}"));
                }
            }
        }

        if (examined == 0)
        {
            return CheckResult.Skip("D1", "No answer to check; S3 reports it.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("D1", Invariant($"All {count} probabilities are finite and in [0, 1]."))
            : CheckResult.Failed("D1", Sentence(problems));
    }

    private static CheckResult ChatD2(ProbeOutcome outcome)
    {
        if (outcome.Response is not { } response)
        {
            return CheckResult.Skip("D2", MinosError);
        }

        var examined = 0;
        var problems = new List<string>();
        foreach (var (question, answer) in ReadAnswers(outcome.Probe, response))
        {
            if (question.Kind == QuestionKind.Noul)
            {
                continue;
            }

            examined++;
            var probabilities = answer.Probabilities;
            if (probabilities.Length != question.Options.Count)
            {
                problems.Add(Invariant($"'{question.Key}' has {probabilities.Length} probabilities for {question.Options.Count} options"));
            }

            var sum = 0.0;
            foreach (var probability in probabilities)
            {
                sum += probability;
            }

            if (!Within(sum, 1))
            {
                problems.Add(Invariant($"'{question.Key}' sums to {sum:0.####}"));
            }
        }

        if (examined == 0)
        {
            return CheckResult.Skip("D2", "No answer to check; S3 reports it.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("D2", "Each Choice and Score sums to 1 within 0.01 over exactly the options sent.")
            : CheckResult.Failed("D2", Sentence(problems));
    }

    // A Score's level is its position, as the core reads it.
    private static CheckResult ChatD3(ProbeOutcome outcome)
    {
        if (outcome.Response is not { } response)
        {
            return CheckResult.Skip("D3", MinosError);
        }

        var examined = 0;
        var problems = new List<string>();
        foreach (var (question, answer) in ReadAnswers(outcome.Probe, response))
        {
            if (question.Kind != QuestionKind.Score)
            {
                continue;
            }

            examined++;
            var probabilities = answer.Probabilities;
            var expected = 0.0;
            for (var level = 0; level < probabilities.Length; level++)
            {
                expected += level * probabilities[level];
            }

            if (!Within(answer.Expected, expected))
            {
                problems.Add(Invariant($"'{question.Key}' expects {answer.Expected}, but its probabilities give {expected:0.####}"));
            }
        }

        if (examined == 0)
        {
            return CheckResult.Skip("D3", "No answer to check; S3 reports it.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("D3", "Each Score's expected level equals the sum of level times probability within 0.01.")
            : CheckResult.Failed("D3", Sentence(problems));
    }

    // Minos gives a Choice and a Score a confidence, and a Noul none.
    private static CheckResult ChatD4(ProbeOutcome outcome)
    {
        if (outcome.Response is not { } response)
        {
            return CheckResult.Skip("D4", MinosError);
        }

        var count = 0;
        var problems = new List<string>();
        foreach (var (question, answer) in ReadAnswers(outcome.Probe, response))
        {
            if (question.Kind == QuestionKind.Noul)
            {
                continue;
            }

            count++;
            if (!IsUnit(answer.Confidence))
            {
                problems.Add(Invariant($"'{question.Key}' has confidence {answer.Confidence}"));
            }
        }

        if (count == 0)
        {
            return CheckResult.Skip("D4", "No answer reports a confidence.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("D4", Invariant($"All {count} confidences are in [0, 1]."))
            : CheckResult.Failed("D4", Sentence(problems));
    }

    private static CheckResult ChatM1(ConformanceProbe probe, DecisionResponse response)
    {
        var questions = probe.Definition.Questions;
        if (response.Answers.Count != questions.Count)
        {
            return CheckResult.Skip("M1", "Minos's answers do not match the questions; S3 reports it.");
        }

        var problems = new List<string>();
        var order = new List<string>();
        for (var i = 0; i < questions.Count; i++)
        {
            var key = response.Answers[i].Key;
            order.Add(key);
            if (!string.Equals(key, questions[i].Key, StringComparison.Ordinal))
            {
                problems.Add(Invariant($"Minos put '{key}' where '{questions[i].Key}' was asked"));
            }
        }

        return problems.Count == 0
            ? CheckResult.Passed("M1", Invariant($"Minos matched the {questions.Count} answers by key: {string.Join(", ", order)}."))
            : CheckResult.Failed("M1", Sentence(problems));
    }

    private static CheckResult ChatE1(ProbeOutcome outcome)
    {
        var sent = outcome.Exchanges.Count;
        if (sent > 0)
        {
            return CheckResult.Failed("E1", Invariant($"{sent} {(sent == 1 ? "request was" : "requests were")} sent; a request with no questions is rejected before anything is sent."));
        }

        if (outcome.Error is not { } error)
        {
            return CheckResult.Failed("E1", "Minos returned no error.");
        }

        if (error.Kind != DecisionErrorKind.Validation)
        {
            return CheckResult.Failed("E1", "Minos returned " + error.Kind.ToString() + ", not Validation.");
        }

        return string.IsNullOrWhiteSpace(error.Message)
            ? CheckResult.Failed("E1", "The error has no message.")
            : CheckResult.Passed("E1", "No request sent; Minos returned Validation: " + error.Message);
    }

    private static CheckResult ChatR1(ProbeOutcome outcome, DecisionResponse response, ConformanceTarget target)
    {
        if (outcome.Exchanges.Count == 0)
        {
            return CheckResult.Skip("R1", NotSent(outcome));
        }

        string? named = null;
        var naming = 0;
        foreach (var exchange in outcome.Exchanges)
        {
            if (!exchange.TryGetJson(out var body) || body.ValueKind != JsonValueKind.Object)
            {
                return CheckResult.Skip("R1", "A response body is not a JSON object.");
            }

            if (ModelOf(body) is not { } model)
            {
                if (target.ModelRule is not null)
                {
                    return CheckResult.Failed("R1", "A response names no model.");
                }

                continue;
            }

            if (!string.Equals(response.Model, model, StringComparison.Ordinal))
            {
                return CheckResult.Failed("R1", "A response names " + model + ", but DecisionResponse.Model is " + (response.Model ?? "null") + ".");
            }

            named = model;
            naming++;
        }

        return named is null
            ? CheckResult.Skip("R1", "No response names a model, and the target promises none.")
            : CheckResult.Passed("R1", Invariant($"The {naming} responses that name a model name {named}, and DecisionResponse.Model is the same."));
    }

    private static CheckResult ChatR3(ProbeOutcome outcome)
    {
        if (outcome.Exchanges.Count == 0)
        {
            return CheckResult.Skip("R3", NotSent(outcome));
        }

        string[] names = ["prompt_tokens", "completion_tokens"];
        var totals = new long?[names.Length];
        var reported = 0;
        var problems = new List<string>();
        foreach (var exchange in outcome.Exchanges)
        {
            if (!exchange.TryGetJson(out var body) || body.ValueKind != JsonValueKind.Object)
            {
                return CheckResult.Skip("R3", "A response body is not a JSON object.");
            }

            if (!body.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (usage.ValueKind != JsonValueKind.Object)
            {
                return CheckResult.Failed("R3", "A usage is not a JSON object.");
            }

            reported++;
            for (var i = 0; i < names.Length; i++)
            {
                if (!usage.TryGetProperty(names[i], out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var tokens) && tokens >= 0)
                {
                    totals[i] = (totals[i] ?? 0) + tokens;
                }
                else
                {
                    problems.Add(names[i] + " is " + value.GetRawText());
                }
            }
        }

        if (problems.Count > 0)
        {
            return CheckResult.Failed("R3", "Usage: " + string.Join("; ", problems) + ".");
        }

        if (reported == 0)
        {
            return CheckResult.Skip("R3", "No response reports usage.");
        }

        var counts = new List<string>();
        for (var i = 0; i < names.Length; i++)
        {
            if (totals[i] is { } total)
            {
                counts.Add(Invariant($"{names[i]} {total}"));
            }
        }

        return counts.Count == 0
            ? CheckResult.Skip("R3", "The usage reports no token counts.")
            : CheckResult.Passed("R3", Invariant($"{string.Join(", ", counts)} over {reported} responses."));
    }

    // The exchange that carried the failing status: the one whose status Minos reported, else the first failure to arrive.
    private static ConformanceExchange? ChatErrorExchange(ProbeOutcome outcome)
    {
        ConformanceExchange? first = null;
        foreach (var exchange in outcome.Exchanges)
        {
            if (exchange.IsSuccess)
            {
                continue;
            }

            if (outcome.Error?.StatusCode is { } status && exchange.Status == status)
            {
                return exchange;
            }

            first ??= exchange;
        }

        return first;
    }

    private static ConformanceExchange? ChatRateLimited(ProbeOutcome outcome)
    {
        foreach (var exchange in outcome.Exchanges)
        {
            if (exchange.Status == 429)
            {
                return exchange;
            }
        }

        return null;
    }

    // Pairs each question with Minos's answer for its key, in question order; a question without one is left out, and S3 reports it.
    private static List<(QuestionDefinition Question, QuestionAnswer Answer)> ReadAnswers(ConformanceProbe probe, DecisionResponse response)
    {
        var pairs = new List<(QuestionDefinition, QuestionAnswer)>();
        foreach (var question in probe.Definition.Questions)
        {
            foreach (var answer in response.Answers)
            {
                if (string.Equals(answer.Key, question.Key, StringComparison.Ordinal))
                {
                    pairs.Add((question, answer));
                    break;
                }
            }
        }

        return pairs;
    }

    private static string OptionKey(QuestionDefinition question, int index)
        => index < question.Options.Count ? question.Options[index].Key : index.ToString(CultureInfo.InvariantCulture);

    private static bool IsUnit(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
