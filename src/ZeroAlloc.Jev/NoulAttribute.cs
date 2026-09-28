namespace ZeroAlloc.Jev;

/// <summary>Declares a Noul question: a yes/no question answered with a probability. Apply to a partial <see cref="Noul"/> property.</summary>
/// <param name="instructions">The question, in plain language.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class NoulAttribute(string instructions) : Attribute
{
    /// <summary>Gets the question, in plain language.</summary>
    public string Instructions { get; } = instructions;

    /// <summary>Gets or sets what a yes answer means. Optional.</summary>
    public string? True { get; set; }

    /// <summary>Gets or sets what a no answer means. Optional.</summary>
    public string? False { get; set; }

    /// <summary>Gets or sets the question's wire key. Defaults to the property name in snake_case.</summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets whether the instructions text is a JSON object or array, sent as structured instructions rather than
    /// as text. The text is checked at compile time (JEV108).
    /// </summary>
    public bool Json { get; set; }
}
