namespace ZeroAlloc.Jev;

/// <summary>Describes a Score level. Every level needs one: the API does not accept a level without a description.</summary>
/// <param name="description">What the level means.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class LevelAttribute(string description) : Attribute
{
    /// <summary>Gets what the level means.</summary>
    public string Description { get; } = description;
}
