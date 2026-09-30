namespace ZeroAlloc.Jev;

/// <summary>
/// Identifies an enum Choice question of a set built with <see cref="JevQuestionSetBuilder"/>; pass it to
/// <c>JevAnswers.Get</c> to read its answer. It works only with answers to a set built by the same builder.
/// </summary>
/// <typeparam name="T">The enum whose members are the options.</typeparam>
public readonly struct ChoiceHandle<T>
    where T : struct, Enum
{
    internal ChoiceHandle(object set, int index)
    {
        Set = set;
        Index = index;
    }

    internal object? Set { get; }

    internal int Index { get; }
}
