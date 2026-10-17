namespace Minos.Evaluation;

/// <summary>Why one item has no answers.</summary>
/// <param name="Kind">The error kind name, such as <c>RateLimited</c>, or <c>Cancelled</c> for a foreign cancellation.</param>
/// <param name="StatusCode">The HTTP status code, or <see langword="null"/>.</param>
/// <param name="Message">The error message.</param>
public sealed record ItemError(string Kind, int? StatusCode, string Message);
