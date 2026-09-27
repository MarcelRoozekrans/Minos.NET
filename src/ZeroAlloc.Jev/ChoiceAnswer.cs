namespace ZeroAlloc.Jev;

/// <summary>The answer to a <see cref="ChoiceQuestion"/>.</summary>
public sealed class ChoiceAnswer : JevAnswer
{
    /// <summary>Gets the highest-probability option.</summary>
    public required string Choice { get; init; }

    /// <summary>Gets every option mapped to its probability; the probabilities sum to 1.</summary>
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }

    /// <summary>Gets how certain the model is, from 0 to 1, derived from the probabilities.</summary>
    public required double Confidence { get; init; }
}
