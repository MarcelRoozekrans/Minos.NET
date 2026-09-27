namespace ZeroAlloc.Jev;

/// <summary>The answers to a <see cref="SystemOneRequest"/>, one per question.</summary>
public sealed class SystemOneResponse
{
    /// <summary>Gets the versioned id of the model that answered, e.g. <c>jev-1.13.0</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the answers, keyed by the question ids from the request.</summary>
    public required IReadOnlyDictionary<string, JevAnswer> Answers { get; init; }

    /// <summary>Gets the token usage for the request.</summary>
    public required JevUsage Usage { get; init; }

    /// <summary>Gets the generation id OpenRouter assigns; <see langword="null"/> on TypeSafe's API.</summary>
    public string? Id { get; init; }

    /// <summary>Gets the upstream provider OpenRouter routed to; <see langword="null"/> on TypeSafe's API.</summary>
    public string? Provider { get; init; }
}
