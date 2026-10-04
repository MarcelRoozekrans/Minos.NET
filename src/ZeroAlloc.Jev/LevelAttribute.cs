namespace ZeroAlloc.Jev;

/// <summary>Describes a Score level. Every level needs one: the API does not accept a level without a description.</summary>
/// <param name="description">What the level means.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class LevelAttribute(string description) : Attribute
{
    /// <summary>Gets what the level means.</summary>
    public string Description { get; } = description;

    /// <summary>
    /// Gets or sets texts that belong to this level. With <see cref="NotFor"/>, they turn the description into a
    /// criterion object, <c>{"description", "examples", "not_for"}</c>: a ZeroAlloc.Jev convention the model reads,
    /// not an API field. Empty or <see langword="null"/> sends the plain description.
    /// </summary>
    public string[]? Examples { get; set; }

    /// <summary>Gets or sets texts that do not belong to this level, though they may look as if they do. See <see cref="Examples"/>.</summary>
    public string[]? NotFor { get; set; }
}
