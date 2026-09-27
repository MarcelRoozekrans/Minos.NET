namespace ZeroAlloc.Jev;

/// <summary>A typed Noul answer: the probability that the answer to a yes/no question is yes.</summary>
public readonly struct Noul
{
    /// <summary>Initializes a new instance of the <see cref="Noul"/> struct.</summary>
    /// <param name="probability">The probability, between 0 and 1, that the answer is yes.</param>
    public Noul(double probability) => Probability = probability;

    /// <summary>Gets the probability, between 0 and 1, that the answer is yes.</summary>
    public double Probability { get; }

    /// <summary>Gets a value indicating whether the answer is yes: <see cref="Probability"/> is at least 0.5.</summary>
    public bool Value => Probability >= 0.5;
}
