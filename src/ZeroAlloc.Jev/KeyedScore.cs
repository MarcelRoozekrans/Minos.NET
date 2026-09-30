namespace ZeroAlloc.Jev;

/// <summary>A keyed Score answer: the most probable level of an ordered rubric, with the expected level and confidence.</summary>
public readonly struct KeyedScore : IEquatable<KeyedScore>
{
    /// <summary>Initializes a new instance of the <see cref="KeyedScore"/> struct.</summary>
    /// <param name="level">The most probable level's index.</param>
    /// <param name="expected">The probability-weighted mean level index the API returns as <c>score</c>.</param>
    /// <param name="confidence">The model's confidence, between 0 and 1.</param>
    /// <param name="probabilities">The probability of each level, keyed by index.</param>
    public KeyedScore(int level, double expected, double confidence, KeyedProbabilityMap probabilities)
    {
        Level = level;
        Expected = expected;
        Confidence = confidence;
        Probabilities = probabilities;
    }

    /// <summary>Gets the most probable level's index. On a tie, the lower level.</summary>
    public int Level { get; }

    /// <summary>Gets the probability-weighted mean level index the API returns as <c>score</c>, for example 1.05.</summary>
    public double Expected { get; }

    /// <summary>Gets the model's confidence, between 0 and 1.</summary>
    public double Confidence { get; }

    /// <summary>Gets the probability of each level, by index.</summary>
    public KeyedProbabilityMap Probabilities { get; }

    /// <summary>Compares two scores for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(KeyedScore left, KeyedScore right) => left.Equals(right);

    /// <summary>Compares two scores for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(KeyedScore left, KeyedScore right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(KeyedScore other)
        => Level == other.Level
        && Expected.Equals(other.Expected)
        && Confidence.Equals(other.Confidence)
        && Probabilities.Equals(other.Probabilities);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is KeyedScore other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Level, Expected, Confidence, Probabilities);
}
