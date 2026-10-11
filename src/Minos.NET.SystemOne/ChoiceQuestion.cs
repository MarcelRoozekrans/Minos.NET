namespace Minos.SystemOne;

/// <summary>Picks one option from a set you define.</summary>
public sealed class ChoiceQuestion : Question
{
    /// <summary>
    /// Gets the options, each mapped to a description or <see langword="null"/> when it needs none.
    /// </summary>
    public required IReadOnlyDictionary<string, DecisionContent?> Criteria { get; init; }
}
