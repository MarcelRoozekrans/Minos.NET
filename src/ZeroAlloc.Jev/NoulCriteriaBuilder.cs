namespace ZeroAlloc.Jev;

/// <summary>Describes what a yes and a no mean for a Noul question built with <see cref="JevQuestionSetBuilder"/>.</summary>
public sealed class NoulCriteriaBuilder
{
    private readonly QuestionDraft _draft;

    internal NoulCriteriaBuilder(QuestionDraft draft) => _draft = draft;

    /// <summary>Sets what a yes (a value near 1) means, replacing any description set before.</summary>
    /// <param name="description">Text or JSON.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="description"/> is uninitialized.</exception>
    public NoulCriteriaBuilder WhenTrue(JevContent description)
    {
        JevContent.EnsureInitialized(description, nameof(description));
        _draft.WhenTrue = description;
        return this;
    }

    /// <summary>Sets what a no (a value near 0) means, replacing any description set before.</summary>
    /// <param name="description">Text or JSON.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="description"/> is uninitialized.</exception>
    public NoulCriteriaBuilder WhenFalse(JevContent description)
    {
        JevContent.EnsureInitialized(description, nameof(description));
        _draft.WhenFalse = description;
        return this;
    }
}
