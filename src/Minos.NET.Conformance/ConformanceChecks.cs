using System.Globalization;
using System.Text.Json;

namespace Minos.Conformance;

/// <summary>
/// The checks: pure functions over one probe's exchanges and what Minos returned, except E4, which reads the whole run.
/// Each returns pass, fail or skipped; a check that cannot run is skipped, never passed.
/// </summary>
public static class ConformanceChecks
{
    /// <summary>How far a sum or an expected level may be from its target: 0.01.</summary>
    public const double Tolerance = 0.01;

    // Floating-point slack, so a sum that is 0.01 off on paper, such as 0.5 + 0.49, is still within the tolerance.
    private const double Rounding = 1e-9;

    // The longest wait the core reads from a header; it clamps larger values to it.
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    private static readonly Check[] PerProbe =
    [
        new("S1", IsAnswer, static (outcome, target) => S1(outcome, target.Wire)),
        new("S2", IsAnswer, static (outcome, target) => S2(outcome, target.Wire)),
        new("S3", IsAnswer, static (outcome, _) => S3(outcome)),
        new("D1", IsAnswer, static (outcome, target) => D1(outcome, target.Wire)),
        new("D2", IsAnswer, static (outcome, target) => D2(outcome, target.Wire)),
        new("D3", IsAnswer, static (outcome, target) => D3(outcome, target.Wire)),
        new("D4", IsAnswer, static (outcome, target) => D4(outcome, target.Wire)),
        new("M1", static probe => string.Equals(probe.Id, ConformanceProbe.Mixed.Id, StringComparison.Ordinal), static (outcome, target) => M1(outcome, target.Wire)),
        new("E1", static probe => probe.Kind == ProbeKind.Invalid, static (outcome, _) => E1(outcome)),
        new("E2", static probe => probe.Kind == ProbeKind.BadKey, static (outcome, _) => E2(outcome)),
        new("E3", static probe => probe.Kind != ProbeKind.Answer, static (outcome, _) => E3(outcome)),
        new("R1", IsAnswer, R1),
        new("R2", IsAnswer, R2),
        new("R3", IsAnswer, static (outcome, _) => R3(outcome)),
    ];

    /// <summary>Gets every check id, in the order the report lists them; E4 reads the whole run.</summary>
    public static IReadOnlyList<string> Ids { get; } = ["S1", "S2", "S3", "D1", "D2", "D3", "D4", "M1", "E1", "E2", "E3", "E4", "R1", "R2", "R3"];

    /// <summary>Gets the request-id headers E3 and E4 read, in the order <see cref="DecisionError.RequestId"/> reads them.</summary>
    /// <remarks>The names and their order must match the core's internal <c>RequestIdHeader.Names</c>, which a core test pins.</remarks>
    public static IReadOnlyList<string> RequestIdHeaders { get; } = ["x-request-id", "x-typesafe-request-id", "request-id"];

    /// <summary>Gets the ids of the checks that apply to a probe, in report order.</summary>
    /// <param name="probe">The probe.</param>
    /// <returns>The ids.</returns>
    public static IReadOnlyList<string> For(ConformanceProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var ids = new List<string>();
        foreach (var check in PerProbe)
        {
            if (check.AppliesTo(probe))
            {
                ids.Add(check.Id);
            }
        }

        return ids;
    }

    /// <summary>Runs every check that applies to the outcome's probe.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="target">The target, for its wire format and model rule.</param>
    /// <returns>The results, in report order.</returns>
    public static IReadOnlyList<CheckResult> Run(ProbeOutcome outcome, ConformanceTarget target)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(target);
        var results = new List<CheckResult>();
        foreach (var check in PerProbe)
        {
            if (check.AppliesTo(outcome.Probe))
            {
                results.Add(check.Run(outcome, target));
            }
        }

        return results;
    }

    /// <summary>Skips every check that applies to a probe that did not run.</summary>
    /// <param name="probe">The probe.</param>
    /// <param name="reason">Why it did not run.</param>
    /// <returns>The results, each skipped with <paramref name="reason"/>.</returns>
    public static IReadOnlyList<CheckResult> Skipped(ConformanceProbe probe, string reason)
    {
        var results = new List<CheckResult>();
        foreach (var id in For(probe))
        {
            results.Add(CheckResult.Skip(id, reason));
        }

        return results;
    }

    /// <summary>S1: the response is a JSON object with exactly one answer per question asked.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="wire">The target's wire format.</param>
    /// <returns>The result.</returns>
    public static CheckResult S1(ProbeOutcome outcome, ConformanceWire wire)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Last is { IsSuccess: false } last)
        {
            return CheckResult.Failed("S1", "HTTP " + StatusText(last) + ": the response is an error, not answers.");
        }

        if (!WireAnswers.TryRead(outcome, wire, out var answers, out var problem))
        {
            return outcome.Last is null ? CheckResult.Skip("S1", problem) : CheckResult.Failed("S1", problem);
        }

        var questions = outcome.Probe.Definition.Questions;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var answer in answers)
        {
            if (Question(outcome.Probe, answer.Key) is null)
            {
                problems.Add("an answer for '" + answer.Key + "', which was not asked");
            }
            else if (!seen.Add(answer.Key))
            {
                problems.Add("two answers for '" + answer.Key + "'");
            }
        }

        foreach (var question in questions)
        {
            if (!seen.Contains(question.Key))
            {
                problems.Add("no answer for '" + question.Key + "'");
            }
        }

        return problems.Count == 0
            ? CheckResult.Passed("S1", Invariant($"One answer for each question asked, {questions.Count} in all."))
            : CheckResult.Failed("S1", Sentence(problems));
    }

    /// <summary>S2: each answer's type matches its question: Noul is <c>noul</c> on System One and <c>predicate</c> on Decisions; Choice and Score are <c>choice</c> and <c>score</c> on both.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="wire">The target's wire format.</param>
    /// <returns>The result.</returns>
    public static CheckResult S2(ProbeOutcome outcome, ConformanceWire wire)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!WireAnswers.TryRead(outcome, wire, out var answers, out var problem))
        {
            return CheckResult.Skip("S2", problem);
        }

        var examined = 0;
        var problems = new List<string>();
        foreach (var question in outcome.Probe.Definition.Questions)
        {
            var expected = WireAnswers.TypeOf(question.Kind, wire);
            if (WireAnswers.Find(answers, question.Key) is not { } answer)
            {
                continue;
            }

            examined++;
            if (!string.Equals(answer.Type, expected, StringComparison.Ordinal))
            {
                problems.Add("'" + question.Key + "' is " + (answer.Type is null ? "untyped" : "'" + answer.Type + "'") + ", not '" + expected + "'");
            }
        }

        if (examined == 0)
        {
            return CheckResult.Skip("S2", "No answer to check; S1 reports it.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("S2", "Each answer has its question's type.")
            : CheckResult.Failed("S2", Sentence(problems));
    }

    /// <summary>S3: Minos reads the response without an error, and its result reads every key with its kind.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <returns>The result.</returns>
    public static CheckResult S3(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Error is { } error)
        {
            return CheckResult.Failed("S3", "Minos returned " + error.Kind.ToString() + ": " + error.Message);
        }

        if (outcome.Response is not { } response)
        {
            return CheckResult.Failed("S3", "Minos returned no response.");
        }

        var questions = outcome.Probe.Definition.Questions;
        var read = response.Answers;
        if (read.Count != questions.Count)
        {
            return CheckResult.Failed("S3", Invariant($"Minos read {read.Count} answers for {questions.Count} questions."));
        }

        var problems = new List<string>();
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var answer = read[i];
            if (!string.Equals(answer.Key, question.Key, StringComparison.Ordinal))
            {
                problems.Add(Invariant($"answer {i} is '{answer.Key}', not '{question.Key}'"));
            }
            else if (answer.Kind != question.Kind)
            {
                problems.Add("'" + question.Key + "' was read as a " + answer.Kind.ToString() + ", not a " + question.Kind.ToString());
            }
            else if (question.Kind != QuestionKind.Noul && (answer.ChosenIndex < 0 || answer.ChosenIndex >= question.Options.Count))
            {
                problems.Add(Invariant($"'{question.Key}' chose position {answer.ChosenIndex} of {question.Options.Count}"));
            }
        }

        return problems.Count == 0
            ? CheckResult.Passed("S3", Invariant($"Minos read every answer by key, {questions.Count} in all."))
            : CheckResult.Failed("S3", Sentence(problems));
    }

    /// <summary>D1: every probability is finite and in [0, 1].</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="wire">The target's wire format.</param>
    /// <returns>The result.</returns>
    public static CheckResult D1(ProbeOutcome outcome, ConformanceWire wire)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!WireAnswers.TryRead(outcome, wire, out var answers, out var problem))
        {
            return CheckResult.Skip("D1", problem);
        }

        var count = 0;
        var examined = 0;
        var problems = new List<string>();
        foreach (var question in outcome.Probe.Definition.Questions)
        {
            if (WireAnswers.Find(answers, question.Key) is not { } answer)
            {
                continue;
            }

            examined++;
            if (question.Kind == QuestionKind.Noul)
            {
                count++;
                var value = answer.Number(WireAnswers.NoulProperty(wire));
                if (value is not { } probability)
                {
                    problems.Add("'" + question.Key + "' has no finite " + WireAnswers.NoulProperty(wire));
                }
                else if (probability is < 0 or > 1)
                {
                    problems.Add(Invariant($"'{question.Key}' is {probability}"));
                }

                continue;
            }

            if (answer.Probabilities(wire) is not { } probabilities)
            {
                problems.Add("'" + question.Key + "' has no probabilities");
                continue;
            }

            foreach (var (key, value) in probabilities)
            {
                count++;
                if (value is not { } probability)
                {
                    problems.Add("'" + question.Key + "' gives '" + key + "' no finite number");
                }
                else if (probability is < 0 or > 1)
                {
                    problems.Add(Invariant($"'{question.Key}' gives '{key}' {probability}"));
                }
            }
        }

        if (examined == 0)
        {
            return CheckResult.Skip("D1", "No answer to check; S1 reports it.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("D1", Invariant($"All {count} probabilities are finite and in [0, 1]."))
            : CheckResult.Failed("D1", Sentence(problems));
    }

    /// <summary>D2: a Choice's or Score's probabilities sum to 1 within 0.01 and cover exactly the options or levels sent.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="wire">The target's wire format.</param>
    /// <returns>The result.</returns>
    public static CheckResult D2(ProbeOutcome outcome, ConformanceWire wire)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!Asks(outcome.Probe, QuestionKind.Choice) && !Asks(outcome.Probe, QuestionKind.Score))
        {
            return CheckResult.Skip("D2", "The probe asks no Choice or Score question.");
        }

        if (!WireAnswers.TryRead(outcome, wire, out var answers, out var problem))
        {
            return CheckResult.Skip("D2", problem);
        }

        var examined = 0;
        var problems = new List<string>();
        foreach (var question in outcome.Probe.Definition.Questions)
        {
            if (question.Kind == QuestionKind.Noul || WireAnswers.Find(answers, question.Key) is not { } answer)
            {
                continue;
            }

            examined++;

            if (answer.Probabilities(wire) is not { } probabilities)
            {
                problems.Add("'" + question.Key + "' has no probabilities");
                continue;
            }

            var sent = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in question.Options)
            {
                sent.Add(option.Key);
            }

            var got = new HashSet<string>(StringComparer.Ordinal);
            var sum = 0.0;
            foreach (var (key, value) in probabilities)
            {
                if (!sent.Contains(key))
                {
                    problems.Add("'" + question.Key + "' has a probability for '" + key + "', which was not sent");
                }
                else if (!got.Add(key))
                {
                    problems.Add("'" + question.Key + "' has two probabilities for '" + key + "'");
                }

                sum += value ?? 0;
            }

            foreach (var option in question.Options)
            {
                if (!got.Contains(option.Key))
                {
                    problems.Add("'" + question.Key + "' has no probability for '" + option.Key + "'");
                }
            }

            if (!Within(sum, 1))
            {
                problems.Add(Invariant($"'{question.Key}' sums to {sum:0.####}"));
            }
        }

        if (examined == 0)
        {
            return CheckResult.Skip("D2", "No answer to check; S1 reports it.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("D2", "Each Choice and Score sums to 1 within 0.01 over exactly the options sent.")
            : CheckResult.Failed("D2", Sentence(problems));
    }

    /// <summary>D3: a Score's <c>score</c> equals the sum of level times probability within 0.01.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="wire">The target's wire format.</param>
    /// <returns>The result.</returns>
    public static CheckResult D3(ProbeOutcome outcome, ConformanceWire wire)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!Asks(outcome.Probe, QuestionKind.Score))
        {
            return CheckResult.Skip("D3", "The probe asks no Score question.");
        }

        if (!WireAnswers.TryRead(outcome, wire, out var answers, out var problem))
        {
            return CheckResult.Skip("D3", problem);
        }

        var examined = 0;
        var problems = new List<string>();
        foreach (var question in outcome.Probe.Definition.Questions)
        {
            if (question.Kind != QuestionKind.Score || WireAnswers.Find(answers, question.Key) is not { } answer)
            {
                continue;
            }

            examined++;

            if (answer.Number("score") is not { } score)
            {
                problems.Add("'" + question.Key + "' has no finite score");
                continue;
            }

            if (answer.Probabilities(wire) is not { } probabilities)
            {
                problems.Add("'" + question.Key + "' has no probabilities");
                continue;
            }

            var expected = 0.0;
            var readable = true;
            foreach (var (key, value) in probabilities)
            {
                if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
                {
                    problems.Add("'" + question.Key + "' has a level '" + key + "' that is not a number");
                    readable = false;
                    continue;
                }

                expected += level * (value ?? 0);
            }

            if (readable && !Within(score, expected))
            {
                problems.Add(Invariant($"'{question.Key}' scores {score}, but its probabilities give {expected:0.####}"));
            }
        }

        if (examined == 0)
        {
            return CheckResult.Skip("D3", "No answer to check; S1 reports it.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("D3", "Each Score equals the sum of level times probability within 0.01.")
            : CheckResult.Failed("D3", Sentence(problems));
    }

    /// <summary>D4: a reported confidence is in [0, 1]; skipped when no answer reports one.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="wire">The target's wire format.</param>
    /// <returns>The result.</returns>
    public static CheckResult D4(ProbeOutcome outcome, ConformanceWire wire)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!WireAnswers.TryRead(outcome, wire, out var answers, out var problem))
        {
            return CheckResult.Skip("D4", problem);
        }

        var count = 0;
        var problems = new List<string>();
        foreach (var question in outcome.Probe.Definition.Questions)
        {
            if (WireAnswers.Find(answers, question.Key) is not { } answer || !answer.TryGet("confidence", out _))
            {
                continue;
            }

            count++;
            if (answer.Number("confidence") is not { } confidence)
            {
                problems.Add("'" + question.Key + "' has a confidence that is not a finite number");
            }
            else if (confidence is < 0 or > 1)
            {
                problems.Add(Invariant($"'{question.Key}' has confidence {confidence}"));
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

    /// <summary>M1: the answers are matched by key; a parser or provider relying on order fails.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="wire">The target's wire format.</param>
    /// <returns>The result.</returns>
    public static CheckResult M1(ProbeOutcome outcome, ConformanceWire wire)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Response is not { } response)
        {
            return CheckResult.Skip("M1", "Minos returned an error; S3 reports it.");
        }

        if (!WireAnswers.TryRead(outcome, wire, out var answers, out var problem))
        {
            return CheckResult.Skip("M1", problem);
        }

        var questions = outcome.Probe.Definition.Questions;
        if (response.Answers.Count != questions.Count)
        {
            return CheckResult.Skip("M1", "Minos's answers do not match the questions; S3 reports it.");
        }

        var problems = new List<string>();
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var read = response.Answers[i];
            if (WireAnswers.Find(answers, question.Key) is not { } sent)
            {
                problems.Add("no answer for '" + question.Key + "'");
                continue;
            }

            var mismatch = question.Kind switch
            {
                QuestionKind.Noul => SameNumber(read.Value, sent.Number(WireAnswers.NoulProperty(wire)))
                    ? null
                    : Invariant($"Minos read '{question.Key}' as {read.Value}, but the response says {Shown(sent.Number(WireAnswers.NoulProperty(wire)))}"),
                QuestionKind.Choice => read.ChosenIndex >= 0 && read.ChosenIndex < question.Options.Count
                    && string.Equals(question.Options[read.ChosenIndex].Key, sent.Text("choice"), StringComparison.Ordinal)
                        ? null
                        : "Minos read '" + question.Key + "' as '" + Chosen(question, read) + "', but the response chose '" + (sent.Text("choice") ?? "nothing") + "'",
                _ => SameNumber(read.Value, sent.Number("score"))
                    ? null
                    : Invariant($"Minos read '{question.Key}' as {read.Value}, but the response scores {Shown(sent.Number("score"))}"),
            };
            if (mismatch is not null)
            {
                problems.Add(mismatch);
            }
        }

        var order = new List<string>();
        foreach (var answer in answers)
        {
            order.Add(answer.Key);
        }

        return problems.Count == 0
            ? CheckResult.Passed("M1", Invariant($"Minos matched the {questions.Count} answers by key; the response listed them as {string.Join(", ", order)}."))
            : CheckResult.Failed("M1", Sentence(problems));
    }

    /// <summary>E1: a request the provider must reject returns 400 or 422 with a JSON body that Minos maps to <see cref="DecisionErrorKind.Validation"/>, with a message.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <returns>The result.</returns>
    public static CheckResult E1(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Last is not { } last)
        {
            return CheckResult.Skip("E1", NotSent(outcome));
        }

        if (last.Status is not (400 or 422))
        {
            return CheckResult.Failed("E1", "HTTP " + StatusText(last) + ", not 400 or 422: the provider did not reject the request.");
        }

        if (outcome.Error is not { } error)
        {
            return CheckResult.Failed("E1", "Minos returned no error.");
        }

        if (!last.TryGetJson(out _))
        {
            return CheckResult.Failed("E1", "The error body is not JSON.");
        }

        if (error.Detail is null)
        {
            return CheckResult.Failed("E1", "The error body is JSON, but Minos left DecisionError.Detail null.");
        }

        if (error.Kind != DecisionErrorKind.Validation)
        {
            return CheckResult.Failed("E1", "Minos mapped it to " + error.Kind.ToString() + ", not Validation.");
        }

        return string.IsNullOrWhiteSpace(error.Message)
            ? CheckResult.Failed("E1", "The error has no message.")
            : CheckResult.Passed("E1", "HTTP " + StatusText(last) + " with a JSON body, mapped to Validation: " + error.Message);
    }

    /// <summary>E2: an invalid key returns 401 or 403, mapped to <see cref="DecisionErrorKind.Unauthorized"/>, and is not retried.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <returns>The result.</returns>
    public static CheckResult E2(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Last is not { } last)
        {
            return CheckResult.Skip("E2", NotSent(outcome));
        }

        if (last.Status is not (401 or 403))
        {
            return CheckResult.Failed("E2", "HTTP " + StatusText(last) + ", not 401 or 403.");
        }

        if (outcome.Error is not { Kind: DecisionErrorKind.Unauthorized })
        {
            return CheckResult.Failed("E2", "Minos mapped it to " + (outcome.Error?.Kind.ToString() ?? "a response") + ", not Unauthorized.");
        }

        return outcome.Exchanges.Count == 1
            ? CheckResult.Passed("E2", "HTTP " + StatusText(last) + ", mapped to Unauthorized, one request sent under the client's retry setting.")
            : CheckResult.Failed("E2", Invariant($"The request was sent {outcome.Exchanges.Count} times; an Unauthorized error is not retried."));
    }

    /// <summary>E3: an error response's request-id header, when present, equals <see cref="DecisionError.RequestId"/>; skipped when it carries none.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <returns>The result.</returns>
    public static CheckResult E3(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Last is not { IsSuccess: false } last)
        {
            return CheckResult.Skip("E3", "No error response.");
        }

        if (RequestIdOf(last) is not { } requestId)
        {
            return CheckResult.Skip("E3", "The error response carries no request-id header: x-request-id, x-typesafe-request-id or request-id.");
        }

        var (name, value) = requestId;

        if (outcome.Error is not { } error)
        {
            return CheckResult.Failed("E3", "Minos returned no error.");
        }

        return string.Equals(error.RequestId, value, StringComparison.Ordinal)
            ? CheckResult.Passed("E3", name + " " + value + " is DecisionError.RequestId.")
            : CheckResult.Failed("E3", name + " is " + value + ", but DecisionError.RequestId is " + (error.RequestId ?? "null") + ".");
    }

    /// <summary>
    /// E4: a 429 met during the run has its <c>Retry-After</c>, or <c>retry-after-ms</c>, reflected in
    /// <see cref="DecisionError.RetryAfter"/> and its request id read. A 429 cannot be provoked, so it is skipped when none occurs.
    /// </summary>
    /// <param name="outcomes">Every probe's outcome.</param>
    /// <returns>The result.</returns>
    public static CheckResult E4(IReadOnlyList<ProbeOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var met = 0;
        var problems = new List<string>();
        foreach (var outcome in outcomes)
        {
            if (outcome.Last is not { Status: 429 } last)
            {
                continue;
            }

            met++;
            var probe = outcome.Probe.Id;
            if (outcome.Error is not { Kind: DecisionErrorKind.RateLimited } error)
            {
                problems.Add(probe + ": Minos returned no RateLimited error");
                continue;
            }

            var (present, exact) = RetryAfterOf(last);
            if (present && exact is { } wait && error.RetryAfter != wait)
            {
                problems.Add(Invariant($"{probe}: Retry-After asks {wait}, but DecisionError.RetryAfter is {error.RetryAfter?.ToString() ?? "null"}"));
            }
            else if (present && error.RetryAfter is null)
            {
                problems.Add(probe + ": the Retry-After was not read");
            }

            if (RequestIdOf(last) is { } requestId && !string.Equals(error.RequestId, requestId.Value, StringComparison.Ordinal))
            {
                problems.Add(probe + ": " + requestId.Name + " is " + requestId.Value + ", but DecisionError.RequestId is " + (error.RequestId ?? "null"));
            }
        }

        if (met == 0)
        {
            return CheckResult.Skip("E4", "No 429 was met during the run.");
        }

        return problems.Count == 0
            ? CheckResult.Passed("E4", Invariant($"Every 429 met, {met} in all, had its Retry-After and request id read."))
            : CheckResult.Failed("E4", Sentence(problems));
    }

    /// <summary>R1: a successful response names a model, and <see cref="DecisionResponse.Model"/> equals it.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="target">The target; one without a model rule promises no model.</param>
    /// <returns>The result.</returns>
    public static CheckResult R1(ProbeOutcome outcome, ConformanceTarget target)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(target);
        if (outcome.Response is not { } response)
        {
            return CheckResult.Skip("R1", "Minos returned an error; S3 reports it.");
        }

        if (!WireAnswers.TryBody(outcome, out var body, out var problem))
        {
            return CheckResult.Skip("R1", problem);
        }

        if (ModelOf(body) is not { } model)
        {
            return target.ModelRule is null
                ? CheckResult.Skip("R1", "The response names no model, and the target promises none.")
                : CheckResult.Failed("R1", "The response names no model.");
        }

        return string.Equals(response.Model, model, StringComparison.Ordinal)
            ? CheckResult.Passed("R1", "The response names " + model + ", and DecisionResponse.Model is the same.")
            : CheckResult.Failed("R1", "The response names " + model + ", but DecisionResponse.Model is " + (response.Model ?? "null") + ".");
    }

    /// <summary>R2: the reported model matches the requested one by the target's <see cref="ModelRule"/>.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <param name="target">The target, with the requested model and its rule.</param>
    /// <returns>The result.</returns>
    public static CheckResult R2(ProbeOutcome outcome, ConformanceTarget target)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(target);
        if (target.ModelRule is not { } rule)
        {
            return CheckResult.Skip("R2", "The target states no model rule.");
        }

        if (outcome.Response is null)
        {
            return CheckResult.Skip("R2", "Minos returned an error; S3 reports it.");
        }

        if (!WireAnswers.TryBody(outcome, out var body, out var problem))
        {
            return CheckResult.Skip("R2", problem);
        }

        if (ModelOf(body) is not { } model)
        {
            return CheckResult.Skip("R2", "The response names no model; R1 reports it.");
        }

        return rule.Matches(target.Model, model)
            ? CheckResult.Passed("R2", model + " answers " + target.Model + ".")
            : CheckResult.Failed("R2", model + " does not fit " + target.Model + ". " + rule.Description);
    }

    /// <summary>R3: usage, when present, has non-negative whole token counts.</summary>
    /// <param name="outcome">What the probe did.</param>
    /// <returns>The result.</returns>
    public static CheckResult R3(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Response is null)
        {
            return CheckResult.Skip("R3", "Minos returned an error; S3 reports it.");
        }

        if (!WireAnswers.TryBody(outcome, out var body, out var problem))
        {
            return CheckResult.Skip("R3", problem);
        }

        if (!body.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
        {
            return CheckResult.Skip("R3", "The response reports no usage.");
        }

        if (usage.ValueKind != JsonValueKind.Object)
        {
            return CheckResult.Failed("R3", "The usage is not a JSON object.");
        }

        var counts = new List<string>();
        var problems = new List<string>();
        foreach (var property in usage.EnumerateObject())
        {
            if (!property.Name.EndsWith("_tokens", StringComparison.Ordinal))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var tokens) && tokens >= 0)
            {
                counts.Add(Invariant($"{property.Name} {tokens}"));
            }
            else
            {
                problems.Add(property.Name + " is " + property.Value.GetRawText());
            }
        }

        if (problems.Count > 0)
        {
            return CheckResult.Failed("R3", "Usage: " + string.Join("; ", problems) + ".");
        }

        return counts.Count == 0
            ? CheckResult.Skip("R3", "The usage reports no token counts.")
            : CheckResult.Passed("R3", string.Join(", ", counts) + ".");
    }

    private static bool IsAnswer(ConformanceProbe probe) => probe.Kind == ProbeKind.Answer;

    private static bool Asks(ConformanceProbe probe, QuestionKind kind)
    {
        foreach (var question in probe.Definition.Questions)
        {
            if (question.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static QuestionDefinition? Question(ConformanceProbe probe, string key)
    {
        foreach (var question in probe.Definition.Questions)
        {
            if (string.Equals(question.Key, key, StringComparison.Ordinal))
            {
                return question;
            }
        }

        return null;
    }

    private static string? ModelOf(JsonElement body)
        => body.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String && model.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static (string Name, string Value)? RequestIdOf(ConformanceExchange exchange)
    {
        foreach (var name in RequestIdHeaders)
        {
            if (exchange.Header(name) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return (name, value.Trim());
            }
        }

        return null;
    }

    // retry-after-ms wins, as Minos reads it; a delta-seconds Retry-After gives an exact wait, an HTTP date only a present one.
    private static (bool Present, TimeSpan? Exact) RetryAfterOf(ConformanceExchange exchange)
    {
        if (exchange.Header("retry-after-ms") is { } milliseconds
            && double.TryParse(milliseconds.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var ms)
            && double.IsFinite(ms))
        {
            return (true, ms >= MaxDelay.TotalMilliseconds ? MaxDelay : TimeSpan.FromMilliseconds(ms));
        }

        if (exchange.Header("retry-after") is not { } value)
        {
            return (false, null);
        }

        var text = value.Trim();
        if (text.Length == 0 || !text.All(char.IsAsciiDigit))
        {
            return (true, null);
        }

        // Like the core, an oversized count of seconds is clamped, and a digit string too long for a ulong is the maximum.
        return (true, ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds <= (ulong)(int.MaxValue / 1000)
            ? TimeSpan.FromSeconds(seconds)
            : MaxDelay);
    }

    private static string NotSent(ProbeOutcome outcome)
        => "No request was sent" + (outcome.Error is { } error ? ": " + error.Kind.ToString() + ", " + error.Message : ".");

    private static string StatusText(ConformanceExchange exchange)
        => exchange.Status?.ToString(CultureInfo.InvariantCulture) ?? "without a status";

    private static string Chosen(QuestionDefinition question, QuestionAnswer answer)
        => answer.ChosenIndex >= 0 && answer.ChosenIndex < question.Options.Count ? question.Options[answer.ChosenIndex].Key : "nothing";

    private static bool Within(double actual, double expected) => Math.Abs(actual - expected) <= Tolerance + Rounding;

    private static bool SameNumber(double read, double? sent) => sent is { } value && Math.Abs(read - value) <= Rounding;

    private static string Shown(double? value) => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "nothing";

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private static string Sentence(List<string> problems)
    {
        var text = string.Join("; ", problems);
        return string.Concat(text[..1].ToUpperInvariant(), text.AsSpan(1), ".");
    }

    private sealed record Check(string Id, Func<ConformanceProbe, bool> AppliesTo, Func<ProbeOutcome, ConformanceTarget, CheckResult> Run);
}
