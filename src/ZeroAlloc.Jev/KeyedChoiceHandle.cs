namespace ZeroAlloc.Jev;

/// <summary>
/// Identifies a keyed Choice question of a set built with <see cref="JevQuestionSetBuilder"/>; pass it to
/// <see cref="JevAnswers"/> to read its answer. It works only with answers to a set built by the same builder.
/// </summary>
public readonly struct KeyedChoiceHandle
{
    internal KeyedChoiceHandle(object set, int index)
    {
        Set = set;
        Index = index;
    }

    internal object? Set { get; }

    internal int Index { get; }
}
