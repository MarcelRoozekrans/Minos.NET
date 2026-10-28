namespace Minos.Evaluation;

/// <summary>
/// One answer of a run, reduced to what scoring needs. It is made with <see cref="ForNoul(double, ProbabilitySource)"/>,
/// <see cref="ForChoice(string, double, ProbabilitySource)"/> or <see cref="ForScore(int, double, ProbabilitySource)"/>,
/// or their overloads without a source, so a Noul answer's value and confidence always agree with its probability. It
/// keeps the <see cref="Minos.ProbabilitySource"/> of the answer it was made from.
/// </summary>
public sealed class AnswerRecord
{
    private AnswerRecord(QuestionKind kind, bool noul, double? probability, string? choiceKey, int scoreLevel, double confidence, ProbabilitySource probabilitySource)
    {
        Kind = kind;
        Noul = noul;
        Probability = probability;
        ChoiceKey = choiceKey;
        ScoreLevel = scoreLevel;
        Confidence = confidence;
        ProbabilitySource = probabilitySource;
    }

    /// <summary>Gets the kind of question.</summary>
    public QuestionKind Kind { get; }

    /// <summary>Gets the answer of a Noul question, that is a probability of at least 0.5; <see langword="false"/> for the other kinds.</summary>
    public bool Noul { get; }

    /// <summary>Gets the probability of a Noul question; <see langword="null"/> for the other kinds.</summary>
    public double? Probability { get; }

    /// <summary>Gets the key of the chosen option of a Choice question; <see langword="null"/> for the other kinds.</summary>
    public string? ChoiceKey { get; }

    /// <summary>Gets the chosen level of a Score question, from 0; 0 for the other kinds.</summary>
    public int ScoreLevel { get; }

    /// <summary>Gets the confidence in the answer: the larger of p and 1 - p for a Noul question, the reported confidence otherwise.</summary>
    public double Confidence { get; }

    /// <summary>
    /// Gets how the answer's probabilities were made. <see cref="ProbabilitySource.Verbalized"/> means the model stated them
    /// in its text, so the probability and confidence are not a measured distribution.
    /// </summary>
    public ProbabilitySource ProbabilitySource { get; }

    /// <summary>Creates the answer of a Noul question from its probability, made by the model itself.</summary>
    /// <param name="probability">The probability that the answer is yes, from 0 to 1.</param>
    /// <returns>An answer whose value is <c>probability &gt;= 0.5</c> and whose confidence is the larger of p and 1 - p.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="probability"/> is not a number from 0 to 1.</exception>
    public static AnswerRecord ForNoul(double probability) => ForNoul(probability, ProbabilitySource.Model);

    /// <summary>Creates the answer of a Noul question from its probability.</summary>
    /// <param name="probability">The probability that the answer is yes, from 0 to 1.</param>
    /// <param name="probabilitySource">How the probability was made.</param>
    /// <returns>An answer whose value is <c>probability &gt;= 0.5</c> and whose confidence is the larger of p and 1 - p.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="probability"/> is not a number from 0 to 1, or <paramref name="probabilitySource"/> is not a defined value.
    /// </exception>
    public static AnswerRecord ForNoul(double probability, ProbabilitySource probabilitySource)
    {
        RequireUnit(probability, nameof(probability));
        RequireDefined(probabilitySource);
        return new AnswerRecord(QuestionKind.Noul, probability >= 0.5, probability, null, 0, Math.Max(probability, 1 - probability), probabilitySource);
    }

    /// <summary>Creates the answer of a Choice question, whose probabilities the model made itself.</summary>
    /// <param name="key">The key of the chosen option.</param>
    /// <param name="confidence">The confidence the model reported, from 0 to 1.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="confidence"/> is not a number from 0 to 1.</exception>
    public static AnswerRecord ForChoice(string key, double confidence) => ForChoice(key, confidence, ProbabilitySource.Model);

    /// <summary>Creates the answer of a Choice question.</summary>
    /// <param name="key">The key of the chosen option.</param>
    /// <param name="confidence">The confidence the model reported, from 0 to 1.</param>
    /// <param name="probabilitySource">How the probabilities were made.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="confidence"/> is not a number from 0 to 1, or <paramref name="probabilitySource"/> is not a defined value.
    /// </exception>
    public static AnswerRecord ForChoice(string key, double confidence, ProbabilitySource probabilitySource)
    {
        ArgumentNullException.ThrowIfNull(key);
        RequireUnit(confidence, nameof(confidence));
        RequireDefined(probabilitySource);
        return new AnswerRecord(QuestionKind.Choice, false, null, key, 0, confidence, probabilitySource);
    }

    /// <summary>Creates the answer of a Score question, whose probabilities the model made itself.</summary>
    /// <param name="level">The chosen level, from 0.</param>
    /// <param name="confidence">The confidence the model reported, from 0 to 1.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> is negative, or <paramref name="confidence"/> is not a number from 0 to 1.
    /// </exception>
    public static AnswerRecord ForScore(int level, double confidence) => ForScore(level, confidence, ProbabilitySource.Model);

    /// <summary>Creates the answer of a Score question.</summary>
    /// <param name="level">The chosen level, from 0.</param>
    /// <param name="confidence">The confidence the model reported, from 0 to 1.</param>
    /// <param name="probabilitySource">How the probabilities were made.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> is negative, <paramref name="confidence"/> is not a number from 0 to 1, or
    /// <paramref name="probabilitySource"/> is not a defined value.
    /// </exception>
    public static AnswerRecord ForScore(int level, double confidence, ProbabilitySource probabilitySource)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        RequireUnit(confidence, nameof(confidence));
        RequireDefined(probabilitySource);
        return new AnswerRecord(QuestionKind.Score, false, null, null, level, confidence, probabilitySource);
    }

    private static void RequireUnit(double value, string name)
    {
        if (!ResultsRules.IsUnit(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be a number from 0 to 1.");
        }
    }

    private static void RequireDefined(ProbabilitySource probabilitySource)
    {
        if (!ResultsRules.IsDefined(probabilitySource))
        {
            throw new ArgumentOutOfRangeException(nameof(probabilitySource), probabilitySource, "The probability source must be Model, Logprobs or Verbalized.");
        }
    }
}
