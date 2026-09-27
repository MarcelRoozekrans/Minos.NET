namespace ZeroAlloc.Jev;

/// <summary>Picks one option from a set you define.</summary>
public sealed class ChoiceQuestion : JevQuestion
{
    /// <summary>
    /// Gets the options, each mapped to a description or <see langword="null"/> when it needs none.
    /// </summary>
    public required IReadOnlyDictionary<string, JevContent?> Criteria { get; init; }
}
