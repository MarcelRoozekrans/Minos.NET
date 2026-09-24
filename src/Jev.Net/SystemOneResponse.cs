namespace Jev.Net;

/// <summary>The answers to a <see cref="SystemOneRequest"/>, one per question.</summary>
public sealed record SystemOneResponse
{
    /// <summary>Gets the versioned id of the model that answered, e.g. <c>jev-1.13.0</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the answers, keyed by the question ids from the request.</summary>
    public required IReadOnlyDictionary<string, JevAnswer> Answers { get; init; }

    /// <summary>Gets the token usage for the request.</summary>
    public required JevUsage Usage { get; init; }
}
