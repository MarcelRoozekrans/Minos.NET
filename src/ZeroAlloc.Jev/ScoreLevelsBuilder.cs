using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ZeroAlloc.Jev.Validation;

namespace ZeroAlloc.Jev;

/// <summary>
/// Gives the levels of an enum Score question built with <see cref="JevQuestionSetBuilder"/>, lowest first: level
/// <c>i</c> is the member of the <c>i</c>-th <see cref="Level"/> call. Every distinct member must be given exactly once
/// (JEV104, JEV106). Listed in declaration order, the levels match the generator's for the same enum.
/// </summary>
/// <typeparam name="T">The enum whose members are the levels; its public fields are read, so the trimmer keeps them.</typeparam>
/// <remarks>Valid only inside its callback: once the question method returns, its methods throw <see cref="InvalidOperationException"/>.</remarks>
public sealed class ScoreLevelsBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>
    where T : struct, Enum
{
    private readonly QuestionDraft _draft;
    private readonly EnumOptionSet<T> _options;

    internal ScoreLevelsBuilder(QuestionDraft draft, EnumOptionSet<T> options)
    {
        _draft = draft;
        _options = options;
    }

    /// <summary>Adds the next level. An alias counts as the member it shares a value with.</summary>
    /// <param name="level">The member this level is.</param>
    /// <param name="criterion">What the level means.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The configurator is used after its callback returned.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="criterion"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a member of <typeparamref name="T"/>.</exception>
    public ScoreLevelsBuilder<T> Level(T level, JevCriterion criterion)
    {
        _draft.EnsureOpen();
        ArgumentNullException.ThrowIfNull(criterion);
        var member = _options.IndexOf(level);
        if (member < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "The value is not a member of the enum.");
        }

        var key = _draft.Options.Count.ToString(CultureInfo.InvariantCulture);
        _draft.Options.Add(new OptionSpec(key, _options.NameAt(member), criterion, member));
        return this;
    }
}
