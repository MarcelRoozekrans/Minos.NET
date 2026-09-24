namespace Jev.Net;

/// <summary>The answer to a <see cref="ScoreQuestion"/>.</summary>
public sealed record ScoreAnswer : JevAnswer
{
    /// <summary>Gets the probability-weighted level; it can land between levels.</summary>
    public required double Score { get; init; }

    /// <summary>Gets each level index, as a string key, mapped back to its description.</summary>
    public required IReadOnlyDictionary<string, string> Legend { get; init; }

    /// <summary>Gets each level index, as a string key, mapped to its probability; the probabilities sum to 1.</summary>
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }

    /// <summary>Gets how certain the model is, from 0 to 1, derived from the probabilities.</summary>
    public required double Confidence { get; init; }
}
