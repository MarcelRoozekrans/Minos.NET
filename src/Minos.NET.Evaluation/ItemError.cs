namespace Minos.Evaluation;

/// <summary>Why one item has no answers.</summary>
public sealed class ItemError
{
    /// <summary>Gets the error kind name, such as <c>RateLimited</c>, or <c>Cancelled</c> for a foreign cancellation.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the HTTP status code, or <see langword="null"/>.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the error message.</summary>
    public required string Message { get; init; }
}
