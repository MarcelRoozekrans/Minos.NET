namespace Minos.Evaluation;

/// <summary>The price of one model, with when and where it was read.</summary>
public sealed class ModelPrice
{
    /// <summary>
    /// Gets the model id or alias. It matches a response model that equals it, or that starts with it followed by
    /// <c>-</c>, such as a dated id.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>Gets the price of one million input tokens.</summary>
    public required double InputPerMillion { get; init; }

    /// <summary>Gets the price of one million output tokens.</summary>
    public required double OutputPerMillion { get; init; }

    /// <summary>Gets the day the price was read.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Gets where the price was read, such as a provider's price list.</summary>
    public required string Source { get; init; }
}
