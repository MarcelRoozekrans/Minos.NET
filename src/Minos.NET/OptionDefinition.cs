namespace Minos;

/// <summary>One option of a Choice question, or one level of a Score question, in a <see cref="QuestionDefinition"/>.</summary>
public sealed class OptionDefinition
{
    /// <summary>Creates an option.</summary>
    /// <param name="key">The option's wire key. Score levels are keyed by their position.</param>
    /// <param name="criterion">What the option means, or <see langword="null"/> for a Choice option left undescribed.</param>
    public OptionDefinition(string key, Criterion? criterion)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Key = key;
        Criterion = criterion;
    }

    /// <summary>Gets the option's wire key.</summary>
    public string Key { get; }

    /// <summary>Gets what the option means, or <see langword="null"/> when it is undescribed.</summary>
    public Criterion? Criterion { get; }
}
