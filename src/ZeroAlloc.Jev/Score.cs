namespace ZeroAlloc.Jev;

/// <summary>A typed Score answer: the most probable level of an ordered rubric, with the expected level and confidence.</summary>
/// <typeparam name="T">The enum whose members are the levels, in rubric order.</typeparam>
public readonly struct Score<T> : IEquatable<Score<T>>
    where T : struct, Enum
{
    /// <summary>Initializes a new instance of the <see cref="Score{T}"/> struct.</summary>
    /// <param name="value">The most probable level.</param>
    /// <param name="expected">The probability-weighted mean level index the API returns as <c>score</c>.</param>
    /// <param name="confidence">The model's confidence, between 0 and 1.</param>
    /// <param name="probabilities">The probability of each level.</param>
    public Score(T value, double expected, double confidence, ProbabilityMap<T> probabilities)
    {
        Value = value;
        Expected = expected;
        Confidence = confidence;
        Probabilities = probabilities;
    }

    /// <summary>Gets the most probable level. On a tie, the lower level.</summary>
    public T Value { get; }

    /// <summary>Gets the probability-weighted mean level index the API returns as <c>score</c>, for example 1.05.</summary>
    public double Expected { get; }

    /// <summary>Gets the model's confidence, between 0 and 1.</summary>
    public double Confidence { get; }

    /// <summary>Gets the probability of each level.</summary>
    public ProbabilityMap<T> Probabilities { get; }

    /// <summary>Compares two scores for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(Score<T> left, Score<T> right) => left.Equals(right);

    /// <summary>Compares two scores for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(Score<T> left, Score<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(Score<T> other)
        => Value.Equals(other.Value)
        && Expected.Equals(other.Expected)
        && Confidence.Equals(other.Confidence)
        && Probabilities.Equals(other.Probabilities);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Score<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Value, Expected, Confidence, Probabilities);
}
