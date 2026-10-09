namespace Minos;

/// <summary>Describes what a yes and a no mean for a Noul question built with <see cref="QuestionSetBuilder"/>.</summary>
/// <remarks>Valid only inside its callback: once the question method returns, its methods throw <see cref="InvalidOperationException"/>.</remarks>
public sealed class NoulCriteriaBuilder
{
    private readonly QuestionDraft _draft;

    internal NoulCriteriaBuilder(QuestionDraft draft) => _draft = draft;

    /// <summary>Sets what a yes (a value near 1) means, replacing any description set before.</summary>
    /// <param name="description">Text or JSON.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The configurator is used after its callback returned.</exception>
    /// <exception cref="ArgumentException"><paramref name="description"/> is uninitialized.</exception>
    public NoulCriteriaBuilder WhenTrue(DecisionContent description)
    {
        _draft.EnsureOpen();
        DecisionContent.EnsureInitialized(description, nameof(description));
        _draft.WhenTrue = description;
        return this;
    }

    /// <summary>Sets what a no (a value near 0) means, replacing any description set before.</summary>
    /// <param name="description">Text or JSON.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The configurator is used after its callback returned.</exception>
    /// <exception cref="ArgumentException"><paramref name="description"/> is uninitialized.</exception>
    public NoulCriteriaBuilder WhenFalse(DecisionContent description)
    {
        _draft.EnsureOpen();
        DecisionContent.EnsureInitialized(description, nameof(description));
        _draft.WhenFalse = description;
        return this;
    }
}
