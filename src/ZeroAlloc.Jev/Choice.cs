namespace ZeroAlloc.Jev;

/// <summary>A typed Choice answer: the option the model picked, its confidence, and every option's probability.</summary>
/// <typeparam name="T">The enum whose members are the options.</typeparam>
public readonly struct Choice<T>
    where T : struct, Enum
{
    /// <summary>Initializes a new instance of the <see cref="Choice{T}"/> struct.</summary>
    /// <param name="value">The option the model picked.</param>
    /// <param name="confidence">The model's confidence, between 0 and 1.</param>
    /// <param name="probabilities">The probability of each option.</param>
    public Choice(T value, double confidence, ProbabilityMap<T> probabilities)
    {
        Value = value;
        Confidence = confidence;
        Probabilities = probabilities;
    }

    /// <summary>Gets the option the model picked.</summary>
    public T Value { get; }

    /// <summary>Gets the model's confidence, between 0 and 1.</summary>
    public double Confidence { get; }

    /// <summary>Gets the probability of each option.</summary>
    public ProbabilityMap<T> Probabilities { get; }
}
