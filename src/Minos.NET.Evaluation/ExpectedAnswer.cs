namespace Minos.Evaluation;

/// <summary>
/// The right answer to one question of a dataset item. The <see langword="default"/> value is a Noul answer of
/// <see langword="false"/>.
/// </summary>
public readonly struct ExpectedAnswer : IEquatable<ExpectedAnswer>
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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is negative.</exception>
    public static ExpectedAnswer ForScore(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        return new(QuestionKind.Score, false, null, level);
    }

    /// <summary>Determines whether two expected answers are equal.</summary>
    /// <param name="left">The first answer.</param>
    /// <param name="right">The second answer.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(ExpectedAnswer left, ExpectedAnswer right) => left.Equals(right);

    /// <summary>Determines whether two expected answers differ.</summary>
    /// <param name="left">The first answer.</param>
    /// <param name="right">The second answer.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(ExpectedAnswer left, ExpectedAnswer right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ExpectedAnswer other)
        => Kind == other.Kind
            && Noul == other.Noul
            && string.Equals(ChoiceKey, other.ChoiceKey, StringComparison.Ordinal)
            && ScoreLevel == other.ScoreLevel;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ExpectedAnswer other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            Kind,
            Noul,
            ChoiceKey is null ? 0 : StringComparer.Ordinal.GetHashCode(ChoiceKey),
            ScoreLevel);

    /// <summary>Gets the kind and the right answer as text, for example <c>Choice card_arrival</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Kind switch
    {
        QuestionKind.Noul => "Noul " + (Noul ? "true" : "false"),
        QuestionKind.Choice => "Choice " + ChoiceKey,
        QuestionKind.Score => "Score " + ScoreLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Kind.ToString(),
    };
}
