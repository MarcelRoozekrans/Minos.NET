namespace ZeroAlloc.Jev;

/// <summary>
/// Identifies a keyed Score question of a set built with <see cref="JevQuestionSetBuilder"/>; pass it to
/// <see cref="JevAnswers"/> to read its answer. It works only with answers to a set built by the same builder.
/// </summary>
public readonly struct KeyedScoreHandle
{
    internal KeyedScoreHandle(object owner, int index)
    {
        Owner = owner;
        Index = index;
    }

    /// <summary>Gets the identity token of the builder that created this handle; every set that builder builds shares it.</summary>
    internal object? Owner { get; }

    internal int Index { get; }
}
