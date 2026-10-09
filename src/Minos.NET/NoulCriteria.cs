using System.Text.Json.Serialization;

namespace Minos;

/// <summary>Descriptions of what a yes and a no mean for a <see cref="NoulQuestion"/>.</summary>
public sealed record NoulCriteria
{
    /// <summary>Gets what a yes (a value near 1) means.</summary>
    [JsonPropertyName("true")]
    public DecisionContent? WhenTrue { get; init; }

    /// <summary>Gets what a no (a value near 0) means.</summary>
    [JsonPropertyName("false")]
    public DecisionContent? WhenFalse { get; init; }
}
