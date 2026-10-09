namespace Minos;

/// <summary>
/// The two thresholds that place a Choice or Score answer's <c>Confidence</c> in a <see cref="ConfidenceTier"/>. Keep one
/// instance per action and size it to what a wrong decision costs: a read-only action can use the defaults, and moving
/// money deserves stricter ones. Treat the values as starting points to tune on your own data.
/// </summary>
/// <remarks>
/// <see langword="default"/> is not a zero threshold: it, <c>new()</c> and <see cref="Default"/> all use 0.5 and 0.9, the
/// cut points TypeSafe's confidence guide uses in its example, so an uninitialized value never classifies every answer as
/// <see cref="ConfidenceTier.High"/>.
/// </remarks>
public readonly struct ConfidenceThresholds : IEquatable<ConfidenceThresholds>
{
    private const double DefaultMedium = 0.5;
    private const double DefaultHigh = 0.9;

    private readonly double _medium;
    private readonly double _high;
    private readonly bool _isSet;

    /// <summary>Initializes a new instance of the <see cref="ConfidenceThresholds"/> struct.</summary>
    /// <param name="medium">The lowest confidence that is <see cref="ConfidenceTier.Medium"/>, between 0 and 1.</param>
    /// <param name="high">The lowest confidence that is <see cref="ConfidenceTier.High"/>, between <paramref name="medium"/> and 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A value is not a number or lies outside 0 to 1, or <paramref name="medium"/> exceeds <paramref name="high"/>.
    /// </exception>
    public ConfidenceThresholds(double medium, double high)
    {
        // Written as negated range checks so NaN, which fails every comparison, is rejected too.
        if (!(medium >= 0.0 && medium <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(medium), medium, "The medium threshold must be between 0 and 1.");
        }

        if (!(high >= 0.0 && high <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(high), high, "The high threshold must be between 0 and 1.");
        }

        if (medium > high)
        {
            throw new ArgumentOutOfRangeException(nameof(medium), medium, "The medium threshold must not exceed the high threshold.");
        }

        _medium = medium;
        _high = high;
        _isSet = true;
    }

    /// <summary>
    /// Gets the default thresholds: <see cref="Medium"/> 0.5 and <see cref="High"/> 0.9, the cut points TypeSafe's confidence
    /// guide uses in its example.
    /// </summary>
    public static ConfidenceThresholds Default => default;

    /// <summary>Gets the lowest confidence that is <see cref="ConfidenceTier.Medium"/>; 0.5 unless set.</summary>
    public double Medium => _isSet ? _medium : DefaultMedium;

    /// <summary>Gets the lowest confidence that is <see cref="ConfidenceTier.High"/>; 0.9 unless set.</summary>
    public double High => _isSet ? _high : DefaultHigh;

    /// <summary>Compares two threshold pairs by their effective values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when both thresholds are equal.</returns>
    public static bool operator ==(ConfidenceThresholds left, ConfidenceThresholds right) => left.Equals(right);

    /// <summary>Compares two threshold pairs by their effective values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when a threshold differs.</returns>
    public static bool operator !=(ConfidenceThresholds left, ConfidenceThresholds right) => !left.Equals(right);

    /// <summary>Places <paramref name="confidence"/> in a tier. A value on a threshold belongs to the higher tier.</summary>
    /// <param name="confidence">A Choice or Score answer's <c>Confidence</c>, between 0 and 1.</param>
    /// <returns>
    /// <see cref="ConfidenceTier.High"/> at or above <see cref="High"/>, <see cref="ConfidenceTier.Medium"/> at or above
    /// <see cref="Medium"/>, otherwise <see cref="ConfidenceTier.Low"/>, which a value that is not a number also gets.
    /// </returns>
    public ConfidenceTier Classify(double confidence)
    {
        if (confidence >= High)
        {
            return ConfidenceTier.High;
        }

        return confidence >= Medium ? ConfidenceTier.Medium : ConfidenceTier.Low;
    }

    /// <inheritdoc />
    public bool Equals(ConfidenceThresholds other) => Medium.Equals(other.Medium) && High.Equals(other.High);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConfidenceThresholds other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Medium, High);
}
