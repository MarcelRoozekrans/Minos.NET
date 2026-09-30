using System.Diagnostics.CodeAnalysis;

namespace ZeroAlloc.Jev;

/// <summary>The answers to a <see cref="JevQuestionSet"/>; read each with the handle its builder method returned.</summary>
/// <remarks>
/// Immutable and safe to share. Reading an answer allocates nothing: every Choice and Score answer is a view over one
/// probability buffer this instance owns.
/// </remarks>
public sealed class JevAnswers
{
    private const string ForeignHandle = "The handle belongs to another question set.";

    private readonly JevQuestionSet _set;
    private readonly double[] _probabilities;
    private readonly AnswerSlot[] _slots;

    internal JevAnswers(JevQuestionSet set, double[] probabilities, AnswerSlot[] slots)
    {
        _set = set;
        _probabilities = probabilities;
        _slots = slots;
    }

    /// <summary>Gets a Noul answer.</summary>
    /// <param name="question">The handle from <see cref="JevQuestionSetBuilder.Noul(string, JevContent, out NoulHandle)"/>.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> belongs to another set.</exception>
    public Noul Get(NoulHandle question) => new(Slot(question.Set, question.Index, nameof(question)).Value);

    /// <summary>Gets an enum Choice answer.</summary>
    /// <typeparam name="T">The enum whose members are the options.</typeparam>
    /// <param name="question">The handle from a <c>Choice&lt;T&gt;</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> belongs to another set.</exception>
    public Choice<T> Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(ChoiceHandle<T> question)
        where T : struct, Enum
    {
        var slot = Slot(question.Set, question.Index, nameof(question));
        var options = (EnumOptionSet<T>)_set.Plan[question.Index].Options!;
        return new Choice<T>(options[slot.ValueIndex], slot.Confidence, new ProbabilityMap<T>(_probabilities, slot.Offset, options));
    }

    /// <summary>Gets an enum Score answer.</summary>
    /// <typeparam name="T">The enum whose members are the levels.</typeparam>
    /// <param name="question">The handle from a <c>Score&lt;T&gt;</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> belongs to another set.</exception>
    public Score<T> Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(ScoreHandle<T> question)
        where T : struct, Enum
    {
        var slot = Slot(question.Set, question.Index, nameof(question));
        var options = (EnumOptionSet<T>)_set.Plan[question.Index].Options!;
        return new Score<T>(options[slot.ValueIndex], slot.Value, slot.Confidence, new ProbabilityMap<T>(_probabilities, slot.Offset, options));
    }

    /// <summary>Gets a keyed Choice answer.</summary>
    /// <param name="question">The handle from a keyed <c>Choice</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> belongs to another set.</exception>
    public KeyedChoice Get(KeyedChoiceHandle question)
    {
        var slot = Slot(question.Set, question.Index, nameof(question));
        var options = (KeyedOptionSet)_set.Plan[question.Index].Options!;
        return new KeyedChoice(options[slot.ValueIndex], slot.Confidence, new KeyedProbabilityMap(_probabilities, slot.Offset, options));
    }

    /// <summary>Gets a keyed Score answer.</summary>
    /// <param name="question">The handle from a keyed <c>Score</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> belongs to another set.</exception>
    public KeyedScore Get(KeyedScoreHandle question)
    {
        var slot = Slot(question.Set, question.Index, nameof(question));
        var options = (KeyedOptionSet)_set.Plan[question.Index].Options!;
        return new KeyedScore(slot.ValueIndex, slot.Value, slot.Confidence, new KeyedProbabilityMap(_probabilities, slot.Offset, options));
    }

    // A handle belongs to this set when its builder built it and its question existed at that build.
    private AnswerSlot Slot(object? set, int index, string paramName)
    {
        if (!ReferenceEquals(set, _set.Identity) || (uint)index >= (uint)_slots.Length)
        {
            throw new ArgumentException(ForeignHandle, paramName);
        }

        return _slots[index];
    }
}
