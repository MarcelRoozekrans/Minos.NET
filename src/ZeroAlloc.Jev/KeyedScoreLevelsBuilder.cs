using System.Globalization;
using ZeroAlloc.Jev.Validation;

namespace ZeroAlloc.Jev;

/// <summary>Adds the levels of a keyed Score question built with <see cref="JevQuestionSetBuilder"/>, lowest first, keyed by index.</summary>
public sealed class KeyedScoreLevelsBuilder
{
    private readonly QuestionDraft _draft;

    internal KeyedScoreLevelsBuilder(QuestionDraft draft) => _draft = draft;

    /// <summary>Adds the next level.</summary>
    /// <param name="criterion">What the level means.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="criterion"/> is <see langword="null"/>.</exception>
    public KeyedScoreLevelsBuilder Level(JevCriterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        var key = _draft.Options.Count.ToString(CultureInfo.InvariantCulture);
        _draft.Options.Add(new OptionSpec(key, key, criterion, -1));
        return this;
    }
}
