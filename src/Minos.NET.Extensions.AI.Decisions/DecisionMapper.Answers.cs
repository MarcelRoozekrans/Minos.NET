using System.Text.Json;
using Microsoft.Extensions.AI;
using ZeroAlloc.Results;
using MinosResponse = Minos.DecisionResponse;

namespace Minos.Extensions.AI.Decisions;

/// <content>
/// The answer conversion, per the answers table of docs/extensions-ai-decisions/mapping.md.
/// </content>
/// <remarks>
/// <para>
/// Minos to upstream, for the Over Minos adapter: every answer declares <see cref="DecisionPrecision.TwoDecimalPlaces"/>,
/// carries no raw representation, and writes the Minos values upstream has no slot for under
/// <see cref="MinosPropertyKeys"/>: the probability source on every answer, the confidence on a Choice or Score, and the
/// chosen level on a Score. A Score's expected level is reported as its <c>Score</c>.
/// </para>
/// <para>
/// Upstream to Minos, for the Over upstream adapter: the <c>minos.</c> keys are read back when present, and otherwise
/// the page's assumptions apply: the probability source is <see cref="ProbabilitySource.Model"/>, never derived from the
/// declared precision; the confidence is the probability of the selected candidate, or of the most probable level for a
/// Score; a Score's chosen level is the most probable level, the lower one on a tie; and the expected level is
/// <c>Score</c>, or <c>ExpectedScore</c> when the provider reported none. The precision, the raw representation and
/// every other property are dropped.
/// </para>
/// <para>
/// Distributions are never repaired. Upstream's answer constructors check them and throw
/// <see cref="DecisionProtocolException"/> with upstream's own message, which names no value, for any refusal: a
/// distribution that does not sum to 1 within the declared rounding or has no normalized values consistent with it, or a
/// score outside the level range or that does not agree with its distribution. On the Over upstream side that exception
/// comes from the upstream client, which built the answers, and the adapter maps it to
/// <see cref="DecisionErrorKind.InvalidResponse"/> with its message. On the Over Minos side the mapper throws it from
/// <see cref="ToUpstreamAnswer"/>, and the adapter catches it and throws a new <see cref="DecisionProtocolException"/>
/// with the same message, whose inner exception is a <c>MinosDecisionException</c> carrying a
/// <see cref="DecisionError"/> of kind <see cref="DecisionErrorKind.InvalidResponse"/> with that message and the caught
/// exception as its <see cref="DecisionError.Exception"/>, as for every other invalid Minos response.
/// </para>
/// </remarks>
internal static partial class DecisionMapper
{
    private const string MismatchMessage = "The answers do not match the questions they belong to.";

    private const string ChosenLevelMessage =
        "The answer's " + MinosPropertyKeys.ChosenLevel + " property is not the position of one of its question's levels.";

    private const string ConfidenceMessage =
        "The answer's " + MinosPropertyKeys.Confidence + " property is not a probability.";

    private const string ProbabilitySourceMessage =
        "The answer's " + MinosPropertyKeys.ProbabilitySource + " property is not model, logprobs or verbalized.";

    /// <summary>Converts a Minos response's answers to upstream answers, one per question in order.</summary>
    /// <param name="response">The Minos response.</param>
    /// <param name="questions">The upstream questions the response answers, in the order they were asked.</param>
    /// <returns>The upstream answers, each declaring <see cref="DecisionPrecision.TwoDecimalPlaces"/>.</returns>
    /// <exception cref="DecisionProtocolException">
    /// The answers do not match the questions, or an upstream answer constructor refused a distribution or a score; see
    /// <see cref="ToUpstreamAnswer"/>. The adapter rethrows it as the class remarks describe.
    /// </exception>
    internal static DecisionAnswer[] ToUpstreamAnswers(MinosResponse response, IReadOnlyList<DecisionQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(questions);

        var answers = response.Answers;
        if (answers.Count != questions.Count)
        {
            throw new DecisionProtocolException(MismatchMessage);
        }

        var converted = new DecisionAnswer[answers.Count];
        for (var i = 0; i < converted.Length; i++)
        {
            converted[i] = ToUpstreamAnswer(answers[i], questions[i]);
        }

        return converted;
    }

    /// <summary>
    /// Converts a Minos answer to an upstream answer for <paramref name="question"/>, with the candidate or level ids of
    /// that question and <see cref="DecisionPrecision.TwoDecimalPlaces"/>.
    /// </summary>
    /// <param name="answer">The Minos answer.</param>
    /// <param name="question">The upstream question it answers.</param>
    /// <returns>The upstream answer.</returns>
    /// <exception cref="DecisionProtocolException">
    /// The answer's key is not the question's id, ordinally; the answer's kind, probability count or chosen position does
    /// not fit the question; or the upstream constructor
    /// refused the answer, for any of its reasons: a distribution that does not sum to 1 within 0.005 per value plus
    /// 0.000001; a distribution with no normalized values consistent with that rounding; a Score whose expected level is
    /// outside the level range, from 0 to the level count minus 1; or a Score whose expected level does not agree with its
    /// distribution under that rounding. The message is upstream's own or the mapper's and names no value. The adapter
    /// never repairs the values; it rethrows the exception as the class remarks describe.
    /// </exception>
    internal static DecisionAnswer ToUpstreamAnswer(QuestionAnswer answer, DecisionQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        // Over Minos pairs answers with questions by position, so a Minos client that reorders them is caught here.
        if (!string.Equals(answer.Key, question.Id, StringComparison.Ordinal))
        {
            throw new DecisionProtocolException(MismatchMessage);
        }

        switch (question)
        {
            case BinaryDecisionQuestion when answer.Kind == QuestionKind.Noul:
                return new BinaryDecisionAnswer(
                    question.Id, answer.Probability, DecisionPrecision.TwoDecimalPlaces, rawRepresentation: null, Properties(answer));
            case ChoiceDecisionQuestion choice when answer.Kind == QuestionKind.Choice && Fits(answer, choice.Candidates.Count):
                var candidates = choice.Candidates;
                var choiceProbabilities = new DecisionProbability[candidates.Count];
                for (var i = 0; i < choiceProbabilities.Length; i++)
                {
                    choiceProbabilities[i] = new DecisionProbability(candidates[i].Id, answer.Probabilities[i]);
                }

                return new ChoiceDecisionAnswer(
                    question.Id, candidates[answer.ChosenIndex].Id, choiceProbabilities, DecisionPrecision.TwoDecimalPlaces,
                    rawRepresentation: null, Properties(answer));
            case ScoreDecisionQuestion score when answer.Kind == QuestionKind.Score && Fits(answer, score.Levels.Count):
                var levels = score.Levels;
                var scoreProbabilities = new DecisionProbability[levels.Count];
                for (var i = 0; i < scoreProbabilities.Length; i++)
                {
                    scoreProbabilities[i] = new DecisionProbability(levels[i].Id, answer.Probabilities[i]);
                }

                return new ScoreDecisionAnswer(
                    question.Id, answer.Expected, scoreProbabilities, DecisionPrecision.TwoDecimalPlaces,
                    rawRepresentation: null, Properties(answer));
            default:
                throw new DecisionProtocolException(MismatchMessage);
        }
    }

    /// <summary>Converts upstream answers to Minos answers for <paramref name="definition"/>, one per question in order.</summary>
    /// <param name="answers">The upstream answers.</param>
    /// <param name="definition">The Minos questions they answer, whose keys are the upstream ids.</param>
    /// <returns>The Minos answers, or <see cref="DecisionErrorKind.InvalidResponse"/>; see <see cref="ToMinosAnswer"/>.</returns>
    internal static Result<QuestionAnswer[], DecisionError> ToMinosAnswers(IReadOnlyList<DecisionAnswer> answers, QuestionSetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(definition);

        var questions = definition.Questions;
        if (answers.Count != questions.Count)
        {
            return Result<QuestionAnswer[], DecisionError>.Failure(Invalid(MismatchMessage));
        }

        var converted = new QuestionAnswer[answers.Count];
        for (var i = 0; i < converted.Length; i++)
        {
            var answer = ToMinosAnswer(answers[i], questions[i]);
            if (answer.IsFailure)
            {
                return Result<QuestionAnswer[], DecisionError>.Failure(answer.Error);
            }

            converted[i] = answer.Value;
        }

        return Result<QuestionAnswer[], DecisionError>.Success(converted);
    }

    /// <summary>Converts an upstream answer to a Minos answer for <paramref name="question"/>.</summary>
    /// <param name="answer">The upstream answer, whose distribution upstream has already checked.</param>
    /// <param name="question">The Minos question it answers, whose option keys are the upstream candidate or level ids.</param>
    /// <returns>
    /// The Minos answer; or <see cref="DecisionErrorKind.InvalidResponse"/> when the answer's id, kind or probability ids
    /// do not match the question, or a <c>minos.</c> key holds a value that is not what the key defines. No message names
    /// a value.
    /// </returns>
    internal static Result<QuestionAnswer, DecisionError> ToMinosAnswer(DecisionAnswer answer, QuestionDefinition question)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(question);

        if (!string.Equals(answer.QuestionId, question.Key, StringComparison.Ordinal))
        {
            return Result<QuestionAnswer, DecisionError>.Failure(Invalid(MismatchMessage));
        }

        var properties = answer.AdditionalProperties;
        if (!TryReadSource(properties, out var source))
        {
            return Result<QuestionAnswer, DecisionError>.Failure(Invalid(ProbabilitySourceMessage));
        }

        switch (answer)
        {
            case BinaryDecisionAnswer binary when question.Kind == QuestionKind.Noul:
                return Result<QuestionAnswer, DecisionError>.Success(QuestionAnswer.Noul(binary.TrueProbability, source));

            case ChoiceDecisionAnswer choice when question.Kind == QuestionKind.Choice:
            {
                if (Probabilities(choice.Probabilities, question) is not { } p)
                {
                    break;
                }

                var selected = IndexOf(question, choice.SelectedCandidateId);
                if (selected < 0)
                {
                    break;
                }

                if (!TryReadConfidence(properties, p[selected], out var confidence))
                {
                    return Result<QuestionAnswer, DecisionError>.Failure(Invalid(ConfidenceMessage));
                }

                return Result<QuestionAnswer, DecisionError>.Success(QuestionAnswer.Choice(selected, confidence, p, source));
            }

            case ScoreDecisionAnswer score when question.Kind == QuestionKind.Score:
            {
                if (Probabilities(score.Probabilities, question) is not { } p)
                {
                    break;
                }

                var mostProbable = MostProbable(p);
                if (!TryReadChosenLevel(properties, p.Length, mostProbable, out var level))
                {
                    return Result<QuestionAnswer, DecisionError>.Failure(Invalid(ChosenLevelMessage));
                }

                if (!TryReadConfidence(properties, p[mostProbable], out var confidence))
                {
                    return Result<QuestionAnswer, DecisionError>.Failure(Invalid(ConfidenceMessage));
                }

                return Result<QuestionAnswer, DecisionError>.Success(
                    QuestionAnswer.Score(level, score.Score ?? score.ExpectedScore, confidence, p, source));
            }
        }

        return Result<QuestionAnswer, DecisionError>.Failure(Invalid(MismatchMessage));
    }

    private static bool Fits(QuestionAnswer answer, int count) => answer.Probabilities.Length == count && answer.ChosenIndex < count;

    private static Dictionary<string, object?> Properties(QuestionAnswer answer)
    {
        var properties = new Dictionary<string, object?>(3, StringComparer.Ordinal);
        if (answer.Kind == QuestionKind.Score)
        {
            properties.Add(MinosPropertyKeys.ChosenLevel, answer.ChosenIndex);
        }

        if (answer.Kind != QuestionKind.Noul)
        {
            properties.Add(MinosPropertyKeys.Confidence, answer.Confidence);
        }

        properties.Add(MinosPropertyKeys.ProbabilitySource, answer.ProbabilitySource switch
        {
            ProbabilitySource.Logprobs => MinosPropertyKeys.Logprobs,
            ProbabilitySource.Verbalized => MinosPropertyKeys.Verbalized,
            _ => MinosPropertyKeys.Model,
        });
        return properties;
    }

    // The values in option order, or null when the ids are not the question's option keys in that order.
    private static double[]? Probabilities(IReadOnlyList<DecisionProbability> probabilities, QuestionDefinition question)
    {
        var options = question.Options;
        if (probabilities.Count != options.Count)
        {
            return null;
        }

        var values = new double[options.Count];
        for (var i = 0; i < values.Length; i++)
        {
            if (!string.Equals(probabilities[i].Id, options[i].Key, StringComparison.Ordinal))
            {
                return null;
            }

            values[i] = probabilities[i].Value;
        }

        return values;
    }

    // Upstream checks that the selected candidate is in the distribution, so -1 is only reached by an answer it did not build.
    private static int IndexOf(QuestionDefinition question, string id)
    {
        var options = question.Options;
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Key, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // The most probable position, the lower one on a tie, as the typed Score answer documents its level.
    private static int MostProbable(double[] p)
    {
        var picked = 0;
        for (var i = 1; i < p.Length; i++)
        {
            if (p[i] > p[picked])
            {
                picked = i;
            }
        }

        return picked;
    }

    private static bool TryReadSource(IReadOnlyDictionary<string, object?>? properties, out ProbabilitySource source)
    {
        source = ProbabilitySource.Model;
        if (properties is null || !properties.TryGetValue(MinosPropertyKeys.ProbabilitySource, out var value))
        {
            return true;
        }

        var text = value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null,
        };

        switch (text)
        {
            case MinosPropertyKeys.Model:
                return true;
            case MinosPropertyKeys.Logprobs:
                source = ProbabilitySource.Logprobs;
                return true;
            case MinosPropertyKeys.Verbalized:
                source = ProbabilitySource.Verbalized;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadConfidence(IReadOnlyDictionary<string, object?>? properties, double assumed, out double confidence)
    {
        confidence = assumed;
        if (properties is null || !properties.TryGetValue(MinosPropertyKeys.Confidence, out var value))
        {
            return true;
        }

        double? number = value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetDouble(out var d) => d,
            _ => null,
        };

        if (number is not (>= 0 and <= 1))
        {
            return false;
        }

        confidence = number.Value;
        return true;
    }

    private static bool TryReadChosenLevel(IReadOnlyDictionary<string, object?>? properties, int count, int assumed, out int level)
    {
        level = assumed;
        if (properties is null || !properties.TryGetValue(MinosPropertyKeys.ChosenLevel, out var value))
        {
            return true;
        }

        long? position = value switch
        {
            int i => i,
            long l => l,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var l) => l,
            _ => null,
        };

        if (position is not { } p || p < 0 || p >= count)
        {
            return false;
        }

        level = (int)p;
        return true;
    }

    private static DecisionError Invalid(string message) => new(DecisionErrorKind.InvalidResponse, message);
}
