namespace Minos.Extensions.AI;

/// <summary>How a <see cref="ChatDecisionClient"/> gets each answer's probabilities from the chat model.</summary>
public enum ProbabilityMode
{
    /// <summary>
    /// Asks for logprobs first. When a response carries none, or no usable option label among them, or the provider
    /// rejects the logprob request as invalid (HTTP 400 or 422, as reasoning models do), that question is asked again
    /// for a verbalized estimate, once, and that call's answer or error decides. Any other failure is returned as is.
    /// Nothing is remembered between calls. Each answer's <see cref="ProbabilitySource"/> says which path made it.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Reads the probabilities from the logprobs of the model's first output token. A response without them fails with
    /// <see cref="DecisionErrorKind.Unsupported"/>, and a rejected request with its own error; it is never asked again.
    /// </summary>
    Logprobs = 1,

    /// <summary>
    /// Asks the model to state each option's probability as JSON, and never asks for logprobs. Use it for a model that
    /// returns no logprobs, such as a reasoning model, to skip the wasted logprob call. It sends no temperature, which
    /// reasoning models reject.
    /// </summary>
    Verbalized = 2,
}
