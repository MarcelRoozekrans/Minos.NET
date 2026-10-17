namespace Minos.Evaluation;

/// <summary>The calibration metrics of one question kind, or of all kinds together.</summary>
public sealed class KindMetrics
{
    internal KindMetrics(
        int scored,
        int answered,
        double? accuracy,
        double? baseRate,
        double? expectedCalibrationError,
        double? expectedCalibrationErrorEqualWidth,
        IReadOnlyList<ReliabilityBin> bins,
        IReadOnlyList<SweepPoint> sweep)
    {
        Scored = scored;
        Answered = answered;
        Accuracy = accuracy;
        BaseRate = baseRate;
        ExpectedCalibrationError = expectedCalibrationError;
        ExpectedCalibrationErrorEqualWidth = expectedCalibrationErrorEqualWidth;
        Bins = bins;
        Sweep = sweep;
    }

    /// <summary>Gets the number of questions with an expected answer.</summary>
    public int Scored { get; }

    /// <summary>Gets the number of scored questions whose request succeeded with an answer.</summary>
    public int Answered { get; }

    /// <summary>Gets the share of answered questions answered correctly, or <see langword="null"/> when none is answered.</summary>
    public double? Accuracy { get; }

    /// <summary>
    /// Gets what a constant answer per question would score: for each question key, the count of its most common
    /// expected answer among answered questions, summed over the keys and divided by <see cref="Answered"/>. For a Noul
    /// question that is the larger of its true and false counts. It is taken from the same answers it is compared with,
    /// over the answered questions only, so it is high when a question has few answers: a question answered once always
    /// matches it. <see langword="null"/> when none is answered.
    /// </summary>
    public double? BaseRate { get; }

    /// <summary>
    /// Gets the count-weighted mean of the gap between accuracy and mean confidence over equal-mass <see cref="Bins"/>,
    /// or <see langword="null"/> when none is answered.
    /// </summary>
    public double? ExpectedCalibrationError { get; }

    /// <summary>Gets the same over ten bins of equal confidence width, or <see langword="null"/> when none is answered.</summary>
    public double? ExpectedCalibrationErrorEqualWidth { get; }

    /// <summary>
    /// Gets up to ten bins of answered questions sorted by confidence, each holding the same number of answers; when the
    /// count does not divide, the first bins hold one more.
    /// </summary>
    public IReadOnlyList<ReliabilityBin> Bins { get; }

    /// <summary>Gets the threshold sweep, at 0.00 to 0.99 in steps of 0.01.</summary>
    public IReadOnlyList<SweepPoint> Sweep { get; }
}
