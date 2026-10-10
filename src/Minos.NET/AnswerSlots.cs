using System.Runtime.InteropServices;

namespace Minos;

/// <summary>
/// The answers a protocol read, one slot per question of a <see cref="QuestionSetDefinition"/>, in its order.
/// Generated <c>Create</c> methods read typed answers from it; it lives only for that call.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct AnswerSlots
{
    private readonly ReadOnlySpan<AnswerSlot> _slots;
    private readonly double[] _probabilities;
    private readonly QuestionSetDefinition _definition;

    internal AnswerSlots(ReadOnlySpan<AnswerSlot> slots, double[] probabilities, QuestionSetDefinition definition, AnswerSlot[]? heapSlots = null)
    {
        _slots = slots;
        _probabilities = probabilities;
        _definition = definition;
        HeapSlots = heapSlots;
    }

    /// <summary>Gets the number of answers.</summary>
    public int Count => _slots.Length;

    internal ReadOnlySpan<AnswerSlot> Slots => _slots;

    /// <summary>Gets the array backing <see cref="Slots"/> when the protocol heap-allocated it, else <see langword="null"/>; a factory that keeps the slots can take it instead of copying.</summary>
    internal AnswerSlot[]? HeapSlots { get; }

    internal double[] Probabilities => _probabilities;

    /// <summary>Reads the Noul answer at <paramref name="index"/>.</summary>
    /// <param name="index">The question's position.</param>
    /// <returns>The answer.</returns>
    public Noul Noul(int index) => new(Slot(index, QuestionKind.Noul).Value);

    /// <summary>Reads the Choice answer at <paramref name="index"/>, mapping options through <paramref name="options"/>.</summary>
    /// <typeparam name="T">The enum whose members are the options.</typeparam>
    /// <param name="index">The question's position.</param>
    /// <param name="options">The enum's option set.</param>
    /// <returns>The answer.</returns>
    public Choice<T> Choice<T>(int index, DecisionOptionSet<T> options)
        where T : struct, Enum
    {
        var slot = Slot(index, QuestionKind.Choice);
        CheckOptions(index, options);
        return new Choice<T>(options[slot.ValueIndex], slot.Confidence, new ProbabilityMap<T>(_probabilities, slot.Offset, options));
    }

    /// <summary>Reads the Score answer at <paramref name="index"/>, mapping levels through <paramref name="options"/>.</summary>
    /// <typeparam name="T">The enum whose members are the levels.</typeparam>
    /// <param name="index">The question's position.</param>
    /// <param name="options">The enum's level set.</param>
    /// <returns>The answer.</returns>
    public Score<T> Score<T>(int index, DecisionOptionSet<T> options)
        where T : struct, Enum
    {
        var slot = Slot(index, QuestionKind.Score);
        CheckOptions(index, options);
        return new Score<T>(options[slot.ValueIndex], slot.Value, slot.Confidence, new ProbabilityMap<T>(_probabilities, slot.Offset, options));
    }

    private AnswerSlot Slot(int index, QuestionKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _slots.Length);
        var actual = _definition.QuestionArray[index].Kind;
        if (actual != kind)
        {
            throw new InvalidOperationException($"Question {index} is a {actual}, not a {kind}.");
        }

        return _slots[index];
    }

    private void CheckOptions<T>(int index, DecisionOptionSet<T> options)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(options);
        var expected = _definition.QuestionArray[index].OptionArray.Length;
        if (options.Count != expected)
        {
            throw new ArgumentException($"Question {index} has {expected} options, not {options.Count}.", nameof(options));
        }
    }
}
