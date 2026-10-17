namespace Minos.Evaluation;

/// <summary>What gating on one confidence threshold would give.</summary>
/// <param name="Threshold">The threshold.</param>
/// <param name="Coverage">
/// The share of scored questions answered with a confidence of at least <paramref name="Threshold"/>; a failed request
/// counts as not covered. 0 when nothing is scored.
/// </param>
/// <param name="Accuracy">The accuracy among the covered answers, or <see langword="null"/> when none is covered.</param>
/// <param name="Covered">The number of covered answers.</param>
public sealed record SweepPoint(double Threshold, double Coverage, double? Accuracy, int Covered);
