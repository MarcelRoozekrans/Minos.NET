namespace Minos.Evaluation;

/// <summary>How many successful responses one model gave.</summary>
/// <param name="Model">The response model, or <see langword="null"/> for responses that named none.</param>
/// <param name="Count">The number of responses.</param>
public sealed record ModelCount(string? Model, int Count);
