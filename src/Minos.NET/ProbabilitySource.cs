namespace Minos;

/// <summary>How the probabilities of an answer were made.</summary>
/// <remarks>The values are ordered from the strongest evidence to the weakest, so a larger value is a weaker source.</remarks>
public enum ProbabilitySource
{
    /// <summary>The model reported the probabilities itself, such as in a Minos decisions response.</summary>
    Model = 0,

    /// <summary>The probabilities come from the log probabilities of the generated tokens.</summary>
    Logprobs = 1,

    /// <summary>The model stated the probabilities in its text, so they are not a measured distribution.</summary>
    Verbalized = 2,
}
