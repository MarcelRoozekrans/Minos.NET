namespace Minos.Evaluation;

/// <summary>The price of one model, with when and where it was read.</summary>
/// <param name="Model">
/// The model id or alias. It matches a response model that equals it, or that starts with it followed by <c>-</c>,
/// such as a dated id.
/// </param>
/// <param name="InputPerMillion">The price of one million input tokens.</param>
/// <param name="OutputPerMillion">The price of one million output tokens.</param>
/// <param name="Date">The day the price was read.</param>
/// <param name="Source">Where the price was read, such as a provider's price list.</param>
public sealed record ModelPrice(string Model, double InputPerMillion, double OutputPerMillion, DateOnly Date, string Source);
