namespace Minos.Evaluation;

/// <summary>The calibration metrics of one question kind, or of all kinds together.</summary>
/// <param name="Scored">The number of questions with an expected answer.</param>
/// <param name="Answered">The number of scored questions whose request succeeded with an answer.</param>
/// <param name="Accuracy">The share of answered questions answered correctly, or <see langword="null"/> when none is answered.</param>
/// <param name="BaseRate">
/// What a constant answer would score: the share of the most common expected answer among answered questions, per kind,
/// or <see langword="null"/> when none is answered.
/// </param>
/// <param name="ExpectedCalibrationError">
/// The count-weighted mean of the gap between accuracy and mean confidence over equal-mass <paramref name="Bins"/>,
/// or <see langword="null"/> when none is answered.
/// </param>
/// <param name="ExpectedCalibrationErrorEqualWidth">
/// The same over ten bins of equal confidence width, or <see langword="null"/> when none is answered.
/// </param>
/// <param name="Bins">
/// Up to ten bins of answered questions sorted by confidence, each holding the same number of answers; when the count
/// does not divide, the first bins hold one more.
/// </param>
/// <param name="Sweep">The threshold sweep, at 0.00 to 0.99 in steps of 0.01.</param>
public sealed record KindMetrics(
    int Scored,
    int Answered,
    double? Accuracy,
    double? BaseRate,
    double? ExpectedCalibrationError,
    double? ExpectedCalibrationErrorEqualWidth,
    IReadOnlyList<ReliabilityBin> Bins,
    IReadOnlyList<SweepPoint> Sweep);
