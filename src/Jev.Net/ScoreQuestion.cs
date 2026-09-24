namespace Jev.Net;

/// <summary>Rates the state along an ordered rubric you define.</summary>
public sealed class ScoreQuestion : JevQuestion
{
    /// <summary>Gets the ordered level descriptions, lowest first.</summary>
    public required IReadOnlyList<JevContent> Criteria { get; init; }
}
