using System.Diagnostics.CodeAnalysis;

namespace Minos;

/// <summary>
/// Describes the options of an enum Choice question built with <see cref="JevQuestionSetBuilder"/>. Every distinct
/// member is an option, in declaration order, as the generator sends them; one left undescribed sends
/// <see langword="null"/>.
/// </summary>
/// <typeparam name="T">The enum whose members are the options; its public fields are read, so the trimmer keeps them.</typeparam>
/// <remarks>Valid only inside its callback: once the question method returns, its methods throw <see cref="InvalidOperationException"/>.</remarks>
public sealed class ChoiceOptionsBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>
    where T : struct, Enum
{
    private readonly QuestionDraft _draft;
    private readonly EnumOptionSet<T> _options;

    internal ChoiceOptionsBuilder(QuestionDraft draft, EnumOptionSet<T> options)
    {
        _draft = draft;
        _options = options;
    }

    /// <summary>Describes an option, replacing any description set before. An alias describes the member it shares a value with.</summary>
    /// <param name="option">The option.</param>
    /// <param name="criterion">What the option means.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The configurator is used after its callback returned.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="criterion"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="option"/> is not a member of <typeparamref name="T"/>.</exception>
    public ChoiceOptionsBuilder<T> Describe(T option, JevCriterion criterion)
    {
        _draft.EnsureOpen();
        ArgumentNullException.ThrowIfNull(criterion);
        var index = _options.IndexOf(option);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(option), "The value is not a member of the enum.");
        }

        _draft.Options[index] = _draft.Options[index] with { Criterion = criterion };
        return this;
    }
}
