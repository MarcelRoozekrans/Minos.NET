namespace Minos;

/// <summary>Rates the state along an ordered rubric you define.</summary>
public sealed class ScoreQuestion : Question
{
    /// <summary>Gets the ordered level descriptions, lowest first.</summary>
    public required IReadOnlyList<DecisionContent> Criteria { get; init; }
}
