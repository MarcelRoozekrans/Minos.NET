namespace Minos.Recording;

/// <summary>
/// One answer of a recorded decision: the same members as a <see cref="QuestionAnswer"/>, kept as a value that does
/// not borrow the response's buffers.
/// </summary>
/// <param name="Key">The key of the question that was answered.</param>
/// <param name="Kind">The kind of the question.</param>
/// <param name="ChosenIndex">The chosen option or level, from 0; -1 for a Noul answer.</param>
/// <param name="Probability">The probability of <see langword="true"/> for a Noul answer; 0 for the other kinds.</param>
/// <param name="Expected">The expected level of a Score answer; 0 for the other kinds.</param>
/// <param name="Confidence">The confidence in the answer, from 0 to 1.</param>
/// <param name="Probabilities">The probability of each option or level; empty for a Noul answer.</param>
public readonly record struct RecordedAnswer(
    string Key,
    QuestionKind Kind,
    int ChosenIndex,
    double Probability,
    double Expected,
    double Confidence,
    IReadOnlyList<double> Probabilities)
{
    internal static RecordedAnswer From(QuestionAnswer answer)
        => new(answer.Key, answer.Kind, answer.ChosenIndex, answer.Probability, answer.Expected, answer.Confidence, answer.Probabilities.ToArray());
}
