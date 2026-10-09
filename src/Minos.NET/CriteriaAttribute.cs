namespace Minos;

/// <summary>Describes a Choice option. A member without it is sent with no description.</summary>
/// <param name="description">What the option means.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class CriteriaAttribute(string description) : Attribute
{
    /// <summary>Gets what the option means.</summary>
    public string Description { get; } = description;

    /// <summary>Gets or sets the option's wire key. Defaults to the member name in snake_case.</summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets texts that belong to this option. With <see cref="NotFor"/>, they turn the description into a
    /// criterion object, <c>{"description", "examples", "not_for"}</c>: a Minos.NET convention the model reads,
    /// not an API field. Empty or <see langword="null"/> sends the plain description.
    /// </summary>
    public string[]? Examples { get; set; }

    /// <summary>Gets or sets texts that do not belong to this option, though they may look as if they do. See <see cref="Examples"/>.</summary>
    public string[]? NotFor { get; set; }
}
