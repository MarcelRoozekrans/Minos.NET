namespace Minos.Evaluation;

/// <summary>One bin of the reliability table.</summary>
/// <param name="Count">The number of answers in the bin.</param>
/// <param name="MeanConfidence">The mean confidence of those answers.</param>
/// <param name="Accuracy">The share of those answers that are correct.</param>
public sealed record ReliabilityBin(int Count, double MeanConfidence, double Accuracy);
