namespace Minos;

/// <summary>A keyed Choice answer: the option the model picked, its confidence, and every option's probability.</summary>
public readonly struct KeyedChoice : IEquatable<KeyedChoice>
{
    private readonly string? _value;

    /// <summary>Initializes a new instance of the <see cref="KeyedChoice"/> struct.</summary>
    /// <param name="value">The key of the option the model picked.</param>
    /// <param name="confidence">The model's confidence, between 0 and 1.</param>
    /// <param name="probabilities">The probability of each option.</param>
    public KeyedChoice(string value, double confidence, KeyedProbabilityMap probabilities)
    {
        _value = value;
        Confidence = confidence;
        Probabilities = probabilities;
    }

    /// <summary>Gets the key of the option the model picked; empty for a default instance.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets the model's confidence, between 0 and 1.</summary>
    public double Confidence { get; }

    /// <summary>Gets the probability of each option.</summary>
    public KeyedProbabilityMap Probabilities { get; }

    /// <summary>Compares two choices for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(KeyedChoice left, KeyedChoice right) => left.Equals(right);

    /// <summary>Compares two choices for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(KeyedChoice left, KeyedChoice right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(KeyedChoice other)
        => string.Equals(Value, other.Value, StringComparison.Ordinal)
        && Confidence.Equals(other.Confidence)
        && Probabilities.Equals(other.Probabilities);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is KeyedChoice other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Value), Confidence, Probabilities);
}
