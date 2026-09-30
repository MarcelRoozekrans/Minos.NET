namespace ZeroAlloc.Jev.Validation;

/// <summary>One option of a Choice question, or one level of a Score question, as the rules see it.</summary>
/// <param name="Key">The wire key: the option's key, or the level's index.</param>
internal readonly record struct OptionSpec(string Key);
