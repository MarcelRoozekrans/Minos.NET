namespace Minos.Evaluation;

/// <summary>The failed requests.</summary>
/// <param name="Count">The number of items whose request failed.</param>
/// <param name="ByKind">The number per error kind, sorted by kind, ordinally.</param>
public sealed record ErrorSummary(int Count, IReadOnlyList<KeyValuePair<string, int>> ByKind);
