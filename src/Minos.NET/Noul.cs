namespace Minos;

/// <summary>A typed Noul answer: the probability that the answer to a yes/no question is yes.</summary>
public readonly struct Noul : IEquatable<Noul>
{
    /// <summary>Initializes a new instance of the <see cref="Noul"/> struct.</summary>
    /// <param name="probability">The probability, between 0 and 1, that the answer is yes.</param>
    public Noul(double probability) => Probability = probability;

    /// <summary>Gets the probability, between 0 and 1, that the answer is yes.</summary>
    public double Probability { get; }

    /// <summary>Gets a value indicating whether the answer is yes: <see cref="Probability"/> is at least 0.5.</summary>
    public bool Value => Probability >= 0.5;

    /// <summary>Compares two Noul answers for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(Noul left, Noul right) => left.Equals(right);

    /// <summary>Compares two Noul answers for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(Noul left, Noul right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(Noul other) => Probability.Equals(other.Probability);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Noul other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Probability.GetHashCode();
}
