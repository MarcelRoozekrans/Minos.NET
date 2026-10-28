namespace Minos.Extensions.AI;

/// <summary>How a <see cref="ChatDecisionClient"/> gets each answer's probabilities from the chat model.</summary>
public enum ProbabilityMode
{
    /// <summary>
    /// Asks for logprobs first. When a response carries none, or no usable option label among them, that question is
    /// asked again for a verbalized estimate, once.
    /// Nothing is remembered between calls. Each answer's <see cref="ProbabilitySource"/> says which path made it.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Reads the probabilities from the logprobs of the model's first output token. A response without them fails with
    /// <see cref="DecisionErrorKind.Unsupported"/>; it is never asked again.
    /// </summary>
    Logprobs = 1,

    /// <summary>
    /// Asks the model to state each option's probability as JSON, and never asks for logprobs. Use it for a model that
    /// returns no logprobs, such as a reasoning model, to skip the wasted logprob call.
    /// </summary>
    Verbalized = 2,
}
