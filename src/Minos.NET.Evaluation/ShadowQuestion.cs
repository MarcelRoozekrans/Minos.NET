namespace Minos.Evaluation;

/// <summary>
/// The agreement of the primary and the candidate on one question of a <see cref="ShadowReport"/>. A question is told
/// apart by its key, its kind and its labels, so two definitions that share a key but not their options are two groups.
/// </summary>
public sealed class ShadowQuestion
{
    internal ShadowQuestion(
        string key,
        QuestionKind kind,
        IReadOnlyList<string> labels,
        int compared,
        int primaryOnly,
        int candidateOnly,
        double? agreement,
        double? meanLevelDifference,
        IReadOnlyList<ConfusionCell> confusion,
        IReadOnlyList<KeyValuePair<string, int>> signalsWhenAgreed,
        IReadOnlyList<KeyValuePair<string, int>> signalsWhenDisagreed)
    {
        Key = key;
        Kind = kind;
        Labels = labels;
        Compared = compared;
        PrimaryOnly = primaryOnly;
        CandidateOnly = candidateOnly;
        Agreement = agreement;
        MeanLevelDifference = meanLevelDifference;
        Confusion = confusion;
        SignalsWhenAgreed = signalsWhenAgreed;
        SignalsWhenDisagreed = signalsWhenDisagreed;
    }

    /// <summary>Gets the question's key.</summary>
    public string Key { get; }

    /// <summary>Gets the question's kind.</summary>
    public QuestionKind Kind { get; }

    /// <summary>
    /// Gets the option keys of a Choice question, or the levels of a Score question as <c>0</c>, <c>1</c> and on, in
    /// definition order; empty for a Noul question.
    /// </summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>Gets the number of shadowed decisions in which both sides answered the question.</summary>
    public int Compared { get; }

    /// <summary>Gets the number of shadowed decisions in which only the primary answered the question.</summary>
    public int PrimaryOnly { get; }

    /// <summary>Gets the number of shadowed decisions in which only the candidate answered the question.</summary>
    public int CandidateOnly { get; }

    /// <summary>
    /// Gets the share of the compared decisions in which both sides gave the same answer, or <see langword="null"/> when
    /// none is compared. A Noul answer is yes when its probability is at least 0.5; a Choice answer is its option, and a
    /// Score answer its level.
    /// </summary>
    public double? Agreement { get; }

    /// <summary>
    /// Gets the mean absolute difference of the two sides' levels of a Score question, or <see langword="null"/> for the
    /// other kinds and when none is compared.
    /// </summary>
    public double? MeanLevelDifference { get; }

    /// <summary>
    /// Gets the non-zero cells of the confusion table, the primary's answer by the candidate's, sorted by the primary's
    /// label and then the candidate's, in <see cref="Labels"/> order; empty for a Noul question.
    /// </summary>
    public IReadOnlyList<ConfusionCell> Confusion { get; }

    /// <summary>Gets a value that says the question has fewer than 30 comparisons, too few for its figures to mean much.</summary>
    public bool IsSmall => Compared < 30;

    /// <summary>
    /// Gets the signal outcomes, counted per signal name, of the compared decisions in which the two sides agreed on every
    /// question both answered; sorted by name, ordinally. Signals never become expected answers.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, int>> SignalsWhenAgreed { get; }

    /// <summary>
    /// Gets the signal outcomes, counted per signal name, of the other compared decisions: those in which the two sides
    /// disagreed on at least one question; sorted by name, ordinally.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, int>> SignalsWhenDisagreed { get; }
}
