namespace Minos.Evaluation;

/// <summary>What gating on one confidence threshold would give.</summary>
public sealed class SweepPoint
{
    internal SweepPoint(double threshold, double coverage, double? accuracy, int covered)
    {
        Threshold = threshold;
        Coverage = coverage;
        Accuracy = accuracy;
        Covered = covered;
    }

    /// <summary>Gets the threshold.</summary>
    public double Threshold { get; }

    /// <summary>
    /// Gets the share of scored questions answered with a confidence of at least <see cref="Threshold"/>; a failed
    /// request counts as not covered. 0 when nothing is scored.
    /// </summary>
    public double Coverage { get; }

    /// <summary>Gets the accuracy among the covered answers, or <see langword="null"/> when none is covered.</summary>
    public double? Accuracy { get; }

    /// <summary>Gets the number of covered answers.</summary>
    public int Covered { get; }
}
