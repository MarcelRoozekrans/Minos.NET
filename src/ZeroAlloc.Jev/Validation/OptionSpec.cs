namespace ZeroAlloc.Jev.Validation;

/// <summary>One option of a Choice question, or one level of a Score question, as the rules see it.</summary>
/// <param name="Key">The wire key: the option's key, or the level's index.</param>
/// <param name="Name">What messages call it: the enum member's name, else the key.</param>
/// <param name="Criterion">Its description; <see langword="null"/> for none.</param>
/// <param name="Member">
/// For an enum question, the member's index in <c>EnumOptionSet&lt;T&gt;.ForChoice</c>, which is how an enum Score's
/// levels are matched to its members; <c>-1</c> for a keyed option or level.
/// </param>
internal readonly record struct OptionSpec(string Key, string Name, JevCriterion? Criterion, int Member);
