using Minos;
using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>Decides whether a recorded answer is the expected one, with the same rules the evaluation library scores by.</summary>
internal static class Judge
{
    /// <summary>
    /// Compares a recorded answer to the expected one. A Noul answer is yes at a probability of 0.5 or more and a Choice answer
    /// is its key. An answer of another kind than the expected one is wrong.
    /// </summary>
    /// <param name="answer">The recorded answer.</param>
    /// <param name="expected">The right answer.</param>
    /// <returns><see langword="true"/> when the answer matches.</returns>
    public static bool IsRight(TriageAnswer answer, ExpectedAnswer expected)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return expected.Kind switch
        {
            QuestionKind.Noul when string.Equals(answer.Kind, "noul", StringComparison.Ordinal) && answer.Probability is { } p
                => AnswerRecord.ForNoul(p).Noul == expected.Noul,
            QuestionKind.Choice when string.Equals(answer.Kind, "choice", StringComparison.Ordinal) && answer.ChoiceKey is { } key && answer.Confidence is { } c
                => string.Equals(AnswerRecord.ForChoice(key, c).ChoiceKey, expected.ChoiceKey, StringComparison.Ordinal),
            _ => false,
        };
    }
}
