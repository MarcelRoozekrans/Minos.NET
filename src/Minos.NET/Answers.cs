using System.Diagnostics.CodeAnalysis;

namespace Minos;

/// <summary>The answers to a <see cref="QuestionSet"/>; read each with the handle its builder method returned.</summary>
/// <remarks>
/// Immutable and safe to share. Reading an answer allocates nothing: every Choice and Score answer is a view over one
/// probability buffer this instance owns.
/// </remarks>
public sealed class Answers
{
    private const string ForeignHandle = "The handle belongs to another question set.";

    private readonly QuestionSet _set;
    private readonly double[] _probabilities;
    private readonly AnswerSlot[] _slots;

    internal Answers(QuestionSet set, double[] probabilities, AnswerSlot[] slots)
    {
        _set = set;
        _probabilities = probabilities;
        _slots = slots;
    }

    /// <summary>Gets a Noul answer.</summary>
    /// <param name="question">The handle from <see cref="QuestionSetBuilder.Noul(string, DecisionContent, out NoulHandle)"/>.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> is <c>default</c>, comes from another builder, or names a question added after this set was built.</exception>
    public Noul Get(NoulHandle question) => new(Slot(question.Owner, question.Index, nameof(question)).Value);

    /// <summary>Gets an enum Choice answer.</summary>
    /// <typeparam name="T">The enum whose members are the options.</typeparam>
    /// <param name="question">The handle from a <c>Choice&lt;T&gt;</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> is <c>default</c>, comes from another builder, or names a question added after this set was built.</exception>
    public Choice<T> Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(ChoiceHandle<T> question)
        where T : struct, Enum
    {
        var slot = Slot(question.Owner, question.Index, nameof(question));
        var options = (EnumOptionSet<T>)_set.Plan[question.Index].Options!;
        return new Choice<T>(options[slot.ValueIndex], slot.Confidence, new ProbabilityMap<T>(_probabilities, slot.Offset, options));
    }

    /// <summary>Gets an enum Score answer.</summary>
    /// <typeparam name="T">The enum whose members are the levels.</typeparam>
    /// <param name="question">The handle from a <c>Score&lt;T&gt;</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> is <c>default</c>, comes from another builder, or names a question added after this set was built.</exception>
    public Score<T> Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(ScoreHandle<T> question)
        where T : struct, Enum
    {
        var slot = Slot(question.Owner, question.Index, nameof(question));
        var options = (EnumOptionSet<T>)_set.Plan[question.Index].Options!;
        return new Score<T>(options[slot.ValueIndex], slot.Value, slot.Confidence, new ProbabilityMap<T>(_probabilities, slot.Offset, options));
    }

    /// <summary>Gets a keyed Choice answer.</summary>
    /// <param name="question">The handle from a keyed <c>Choice</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> is <c>default</c>, comes from another builder, or names a question added after this set was built.</exception>
    public KeyedChoice Get(KeyedChoiceHandle question)
    {
        var slot = Slot(question.Owner, question.Index, nameof(question));
        var options = (KeyedOptionSet)_set.Plan[question.Index].Options!;
        return new KeyedChoice(options[slot.ValueIndex], slot.Confidence, new KeyedProbabilityMap(_probabilities, slot.Offset, options));
    }

    /// <summary>Gets a keyed Score answer.</summary>
    /// <param name="question">The handle from a keyed <c>Score</c> builder method.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException"><paramref name="question"/> is <c>default</c>, comes from another builder, or names a question added after this set was built.</exception>
    public KeyedScore Get(KeyedScoreHandle question)
    {
        var slot = Slot(question.Owner, question.Index, nameof(question));
        var options = (KeyedOptionSet)_set.Plan[question.Index].Options!;
        return new KeyedScore(slot.ValueIndex, slot.Value, slot.Confidence, new KeyedProbabilityMap(_probabilities, slot.Offset, options));
    }

    // A handle belongs to this set when its builder built it and its question existed at that build.
    private AnswerSlot Slot(object? owner, int index, string paramName)
    {
        if (!ReferenceEquals(owner, _set.Identity) || (uint)index >= (uint)_slots.Length)
        {
            throw new ArgumentException(ForeignHandle, paramName);
        }

        return _slots[index];
    }
}
