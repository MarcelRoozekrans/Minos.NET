namespace Minos.Recording;

/// <summary>
/// The answer a human or a downstream system confirmed for one question: a Noul value, an option key or a level.
/// It has the shape of an expected answer in a labelled dataset. The <see langword="default"/> value is a Noul answer of
/// <see langword="false"/>.
/// </summary>
public readonly struct ConfirmedAnswer : IEquatable<ConfirmedAnswer>
{
    private ConfirmedAnswer(QuestionKind kind, bool noul, string? choiceKey, int scoreLevel)
    {
        Kind = kind;
        Noul = noul;
        ChoiceKey = choiceKey;
        ScoreLevel = scoreLevel;
    }

    /// <summary>Gets the kind of question this answers.</summary>
    public QuestionKind Kind { get; }

    /// <summary>Gets the confirmed answer of a Noul question; <see langword="false"/> for the other kinds.</summary>
    public bool Noul { get; }

    /// <summary>Gets the confirmed option key of a Choice question; <see langword="null"/> for the other kinds.</summary>
    public string? ChoiceKey { get; }

    /// <summary>Gets the confirmed level of a Score question, from 0; 0 for the other kinds.</summary>
    public int ScoreLevel { get; }

    /// <summary>Creates the confirmed answer of a Noul question.</summary>
    /// <param name="value">The confirmed answer.</param>
    /// <returns>The confirmed answer.</returns>
    public static ConfirmedAnswer ForNoul(bool value) => new(QuestionKind.Noul, value, null, 0);

    /// <summary>Creates the confirmed answer of a Choice question.</summary>
    /// <param name="optionKey">The key of the confirmed option.</param>
    /// <returns>The confirmed answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="optionKey"/> is <see langword="null"/> or empty.</exception>
    public static ConfirmedAnswer ForChoice(string optionKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(optionKey);
        return new(QuestionKind.Choice, false, optionKey, 0);
    }

    /// <summary>Creates the confirmed answer of a Score question.</summary>
    /// <param name="level">The index of the confirmed level, from 0.</param>
    /// <returns>The confirmed answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is negative.</exception>
    public static ConfirmedAnswer ForScore(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        return new(QuestionKind.Score, false, null, level);
    }

    /// <summary>Determines whether two confirmed answers are equal.</summary>
    /// <param name="left">The first answer.</param>
    /// <param name="right">The second answer.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(ConfirmedAnswer left, ConfirmedAnswer right) => left.Equals(right);

    /// <summary>Determines whether two confirmed answers differ.</summary>
    /// <param name="left">The first answer.</param>
    /// <param name="right">The second answer.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(ConfirmedAnswer left, ConfirmedAnswer right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ConfirmedAnswer other)
        => Kind == other.Kind
            && Noul == other.Noul
            && string.Equals(ChoiceKey, other.ChoiceKey, StringComparison.Ordinal)
            && ScoreLevel == other.ScoreLevel;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConfirmedAnswer other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Kind, Noul, ChoiceKey is null ? 0 : StringComparer.Ordinal.GetHashCode(ChoiceKey), ScoreLevel);

    /// <summary>Gets the kind and the confirmed answer as text, for example <c>Choice card_arrival</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Kind switch
    {
        QuestionKind.Noul => "Noul " + (Noul ? "true" : "false"),
        QuestionKind.Choice => "Choice " + ChoiceKey,
        QuestionKind.Score => "Score " + ScoreLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Kind.ToString(),
    };
}
