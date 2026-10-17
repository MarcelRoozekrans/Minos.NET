namespace Minos.Evaluation;

/// <summary>
/// One answer of a run, reduced to what scoring needs. It is made with <see cref="ForNoul"/>, <see cref="ForChoice"/> or
/// <see cref="ForScore"/>, so a Noul answer's value and confidence always agree with its probability.
/// </summary>
public sealed class AnswerRecord
{
    private AnswerRecord(QuestionKind kind, bool noul, double? probability, string? choiceKey, int scoreLevel, double confidence)
    {
        Kind = kind;
        Noul = noul;
        Probability = probability;
        ChoiceKey = choiceKey;
        ScoreLevel = scoreLevel;
        Confidence = confidence;
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

    /// <summary>Creates the answer of a Noul question from its probability.</summary>
    /// <param name="probability">The probability that the answer is yes, from 0 to 1.</param>
    /// <returns>An answer whose value is <c>probability &gt;= 0.5</c> and whose confidence is the larger of p and 1 - p.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="probability"/> is not a number from 0 to 1.</exception>
    public static AnswerRecord ForNoul(double probability)
    {
        RequireUnit(probability, nameof(probability));
        return new AnswerRecord(QuestionKind.Noul, probability >= 0.5, probability, null, 0, Math.Max(probability, 1 - probability));
    }

    /// <summary>Creates the answer of a Choice question.</summary>
    /// <param name="key">The key of the chosen option.</param>
    /// <param name="confidence">The confidence the model reported, from 0 to 1.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="confidence"/> is not a number from 0 to 1.</exception>
    public static AnswerRecord ForChoice(string key, double confidence)
    {
        ArgumentNullException.ThrowIfNull(key);
        RequireUnit(confidence, nameof(confidence));
        return new AnswerRecord(QuestionKind.Choice, false, null, key, 0, confidence);
    }

    /// <summary>Creates the answer of a Score question.</summary>
    /// <param name="level">The chosen level, from 0.</param>
    /// <param name="confidence">The confidence the model reported, from 0 to 1.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> is negative, or <paramref name="confidence"/> is not a number from 0 to 1.
    /// </exception>
    public static AnswerRecord ForScore(int level, double confidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        RequireUnit(confidence, nameof(confidence));
        return new AnswerRecord(QuestionKind.Score, false, null, null, level, confidence);
    }

    private static void RequireUnit(double value, string name)
    {
        if (!ResultsRules.IsUnit(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be a number from 0 to 1.");
        }
    }
}
