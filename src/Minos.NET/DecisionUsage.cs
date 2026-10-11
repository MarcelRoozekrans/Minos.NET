namespace Minos;

/// <summary>Token usage for one request. Only input tokens are billed.</summary>
public sealed record DecisionUsage
{
    /// <summary>Gets the number of input tokens.</summary>
    public required int InputTokens { get; init; }

    /// <summary>Gets the number of output tokens.</summary>
    public required int OutputTokens { get; init; }

    /// <summary>
    /// Gets the cost in US dollars that the provider reports in its response, such as OpenRouter's <c>usage.cost</c>;
    /// <see langword="null"/> when the provider reports none.
    /// </summary>
    public double? Cost { get; init; }
}
