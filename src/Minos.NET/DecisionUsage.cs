namespace Minos;

/// <summary>Token usage, and the cost where the provider reports one, for one request.</summary>
public sealed record DecisionUsage
{
    /// <summary>Gets the number of input tokens, or <see langword="null"/> when the provider reported none.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Gets the number of output tokens, or <see langword="null"/> when the provider reported none.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>
    /// Gets the cost in US dollars that the provider reports in its response, such as OpenRouter's <c>usage.cost</c>;
    /// <see langword="null"/> when the provider reports none.
    /// </summary>
    public double? Cost { get; init; }
}
