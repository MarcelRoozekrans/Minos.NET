namespace Minos.Evaluation;

/// <summary>One non-zero cell of a <see cref="ShadowQuestion"/>'s confusion table: how often the primary gave one answer while the candidate gave another.</summary>
/// <param name="Primary">The primary's answer: an option key of a Choice question, or a level of a Score question.</param>
/// <param name="Candidate">The candidate's answer, labelled the same way.</param>
/// <param name="Count">The number of decisions with this pair of answers.</param>
public readonly record struct ConfusionCell(string Primary, string Candidate, int Count);
