namespace Jev.Net;

/// <summary>Token usage for one request. Only input tokens are billed.</summary>
public sealed record JevUsage
{
    /// <summary>Gets the number of input tokens.</summary>
    public required int InputTokens { get; init; }

    /// <summary>Gets the number of output tokens.</summary>
    public required int OutputTokens { get; init; }
}
