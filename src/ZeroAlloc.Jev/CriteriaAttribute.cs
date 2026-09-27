namespace ZeroAlloc.Jev;

/// <summary>Describes a Choice option. A member without it is sent with no description.</summary>
/// <param name="description">What the option means.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class CriteriaAttribute(string description) : Attribute
{
    /// <summary>Gets what the option means.</summary>
    public string Description { get; } = description;

    /// <summary>Gets or sets the option's wire key. Defaults to the member name in snake_case.</summary>
    public string? Key { get; set; }
}
