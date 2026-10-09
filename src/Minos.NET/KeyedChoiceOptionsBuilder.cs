using Minos.Validation;

namespace Minos;

/// <summary>Adds the options of a keyed Choice question built with <see cref="JevQuestionSetBuilder"/>, in wire order.</summary>
/// <remarks>Valid only inside its callback: once the question method returns, its methods throw <see cref="InvalidOperationException"/>.</remarks>
public sealed class KeyedChoiceOptionsBuilder
{
    private readonly QuestionDraft _draft;

    internal KeyedChoiceOptionsBuilder(QuestionDraft draft) => _draft = draft;

    /// <summary>Adds an option without a description; it sends <see langword="null"/>.</summary>
    /// <param name="key">The option's wire key, which the answer's <see cref="KeyedChoice.Value"/> returns.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The configurator is used after its callback returned.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public KeyedChoiceOptionsBuilder Option(string key)
    {
        _draft.EnsureOpen();
        ArgumentNullException.ThrowIfNull(key);
        _draft.Options.Add(new OptionSpec(key, key, null, -1));
        return this;
    }

    /// <summary>Adds a described option.</summary>
    /// <param name="key">The option's wire key, which the answer's <see cref="KeyedChoice.Value"/> returns.</param>
    /// <param name="criterion">What the option means.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The configurator is used after its callback returned.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="criterion"/> is <see langword="null"/>.</exception>
    public KeyedChoiceOptionsBuilder Option(string key, JevCriterion criterion)
    {
        _draft.EnsureOpen();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(criterion);
        _draft.Options.Add(new OptionSpec(key, key, criterion, -1));
        return this;
    }
}
