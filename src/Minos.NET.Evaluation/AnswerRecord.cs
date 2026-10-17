namespace Minos.Evaluation;

/// <summary>One answer of a run, reduced to what scoring needs.</summary>
/// <param name="Kind">The kind of question.</param>
/// <param name="Noul">The answer of a Noul question, that is a probability of at least 0.5; <see langword="false"/> for the other kinds.</param>
/// <param name="Probability">The probability of a Noul question; <see langword="null"/> for the other kinds.</param>
/// <param name="ChoiceKey">The key of the chosen option of a Choice question; <see langword="null"/> for the other kinds.</param>
/// <param name="ScoreLevel">The chosen level of a Score question, from 0; 0 for the other kinds.</param>
/// <param name="Confidence">The confidence in the answer: the larger of p and 1 - p for a Noul question, the reported confidence otherwise.</param>
public sealed record AnswerRecord(
    QuestionKind Kind,
    bool Noul,
    double? Probability,
    string? ChoiceKey,
    int ScoreLevel,
    double Confidence);
