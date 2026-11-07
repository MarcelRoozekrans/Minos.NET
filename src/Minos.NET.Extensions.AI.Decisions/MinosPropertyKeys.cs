namespace Minos.Extensions.AI.Decisions;

/// <summary>
/// The keys under which the adapters carry Minos values that have no upstream slot in an upstream
/// <c>AdditionalProperties</c> dictionary, as docs/extensions-ai-decisions/mapping.md defines them.
/// </summary>
internal static class MinosPropertyKeys
{
    /// <summary>A Score answer's chosen level, as an <see cref="int"/> position.</summary>
    internal const string ChosenLevel = "minos.chosenLevel";

    /// <summary>A Choice or Score answer's confidence, as a <see cref="double"/> from 0 to 1.</summary>
    internal const string Confidence = "minos.confidence";

    /// <summary>
    /// How an answer's probabilities were made, as one of the strings <see cref="Model"/>, <see cref="Logprobs"/> and
    /// <see cref="Verbalized"/>, the names Minos recordings use.
    /// </summary>
    internal const string ProbabilitySource = "minos.probabilitySource";

    /// <summary>The host an aggregator routed the call to, on a response.</summary>
    internal const string UpstreamProvider = "minos.upstreamProvider";

    /// <summary>The routing path, on a response.</summary>
    internal const string Path = "minos.path";

    /// <summary>The reported cost of the call, on a response.</summary>
    internal const string Cost = "minos.cost";

    /// <summary>Whether a cache stage answered, on a response.</summary>
    internal const string IsCached = "minos.isCached";

    /// <summary>The <see cref="ProbabilitySource"/> value for <see cref="Minos.ProbabilitySource.Model"/>.</summary>
    internal const string Model = "model";

    /// <summary>The <see cref="ProbabilitySource"/> value for <see cref="Minos.ProbabilitySource.Logprobs"/>.</summary>
    internal const string Logprobs = "logprobs";

    /// <summary>The <see cref="ProbabilitySource"/> value for <see cref="Minos.ProbabilitySource.Verbalized"/>.</summary>
    internal const string Verbalized = "verbalized";
}
