namespace ZeroAlloc.Jev;

/// <summary>
/// Identifies a Noul question of a set built with <see cref="JevQuestionSetBuilder"/>; pass it to <c>JevAnswers.Get</c>
/// to read its answer. It works only with answers to a set built by the same builder.
/// </summary>
public readonly struct NoulHandle
{
    internal NoulHandle(object set, int index)
    {
        Set = set;
        Index = index;
    }

    internal object? Set { get; }

    internal int Index { get; }
}
