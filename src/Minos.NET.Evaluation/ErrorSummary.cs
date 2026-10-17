namespace Minos.Evaluation;

/// <summary>The failed requests.</summary>
public sealed class ErrorSummary
{
    internal ErrorSummary(int count, IReadOnlyList<KeyValuePair<string, int>> byKind)
    {
        Count = count;
        ByKind = byKind;
    }

    /// <summary>Gets the number of items whose request failed.</summary>
    public int Count { get; }

    /// <summary>Gets the number per error kind, sorted by kind, ordinally.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> ByKind { get; }
}
