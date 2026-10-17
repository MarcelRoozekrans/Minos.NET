namespace Minos.Evaluation;

/// <summary>The right answer to one question of a dataset item.</summary>
public readonly record struct ExpectedAnswer
{
    private ExpectedAnswer(QuestionKind kind, bool noul, string? choiceKey, int scoreLevel)
    {
        Kind = kind;
        Noul = noul;
        ChoiceKey = choiceKey;
        ScoreLevel = scoreLevel;
    }

    /// <summary>Gets the kind of question this answers.</summary>
    public QuestionKind Kind { get; }

    /// <summary>Gets the expected answer of a Noul question; <see langword="false"/> for the other kinds.</summary>
    public bool Noul { get; }

    /// <summary>Gets the expected option key of a Choice question; <see langword="null"/> for the other kinds.</summary>
    public string? ChoiceKey { get; }

    /// <summary>Gets the expected level index of a Score question, from 0; 0 for the other kinds.</summary>
    public int ScoreLevel { get; }

    /// <summary>Creates the expected answer of a Noul question.</summary>
    /// <param name="value">The right answer.</param>
    /// <returns>The expected answer.</returns>
    public static ExpectedAnswer ForNoul(bool value) => new(QuestionKind.Noul, value, null, 0);

    /// <summary>Creates the expected answer of a Choice question.</summary>
    /// <param name="key">The key of the right option.</param>
    /// <returns>The expected answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static ExpectedAnswer ForChoice(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(QuestionKind.Choice, false, key, 0);
    }

    /// <summary>Creates the expected answer of a Score question.</summary>
    /// <param name="level">The index of the right level, from 0.</param>
    /// <returns>The expected answer.</returns>
    public static ExpectedAnswer ForScore(int level) => new(QuestionKind.Score, false, null, level);
}
