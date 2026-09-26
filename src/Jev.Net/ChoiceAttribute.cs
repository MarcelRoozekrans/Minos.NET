namespace Jev.Net;

/// <summary>
/// Declares a Choice question: pick one option. Apply to a partial <see cref="Choice{T}"/> property; the enum's members
/// are the options, described with <see cref="CriteriaAttribute"/>.
/// </summary>
/// <param name="instructions">The question, in plain language.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ChoiceAttribute(string instructions) : Attribute
{
    /// <summary>Gets the question, in plain language.</summary>
    public string Instructions { get; } = instructions;

    /// <summary>Gets or sets the question's wire key. Defaults to the property name in snake_case.</summary>
    public string? Key { get; set; }
}
