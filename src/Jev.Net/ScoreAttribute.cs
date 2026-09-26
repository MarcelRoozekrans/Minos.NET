namespace Jev.Net;

/// <summary>
/// Declares a Score question: rate on an ordered rubric. Apply to a partial <see cref="Score{T}"/> property; the enum's
/// members, in declaration order, are the levels, each described with <see cref="LevelAttribute"/>.
/// </summary>
/// <param name="instructions">The question, in plain language.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ScoreAttribute(string instructions) : Attribute
{
    /// <summary>Gets the question, in plain language.</summary>
    public string Instructions { get; } = instructions;

    /// <summary>Gets or sets the question's wire key. Defaults to the property name in snake_case.</summary>
    public string? Key { get; set; }
}
