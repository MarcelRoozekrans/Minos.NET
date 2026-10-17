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

    /// <summary>Gets the threshold, <c>i / 100.0</c> for the i-th point.</summary>
    public double Threshold { get; }

    /// <summary>
    /// Gets the share of scored questions answered with a confidence of at least <see cref="Threshold"/>; a failed
    /// request counts as not covered. At the threshold <c>t = i / 100.0</c> an answer is covered when its confidence is
    /// at least t; a Noul answer with probability p is also covered when <c>p &gt;= t</c> or
    /// <c>p &lt;= (100 - i) / 100.0</c>, so that p = 0.07 is covered at 0.93 as p = 0.93 is, though 1 - 0.07 in binary is
    /// just below 0.93. 0 when nothing is scored.
    /// </summary>
    public double Coverage { get; }

    /// <summary>Gets the accuracy among the covered answers, or <see langword="null"/> when none is covered.</summary>
    public double? Accuracy { get; }

    /// <summary>Gets the number of covered answers.</summary>
    public int Covered { get; }
}
