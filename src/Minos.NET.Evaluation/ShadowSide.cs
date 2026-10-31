namespace Minos.Evaluation;

/// <summary>
/// One side of a <see cref="ShadowReport"/>, the primary or the candidate, over the shadowed decisions: who served it,
/// how it failed, how fast and at what cost, and its calibration against the confirmed outcomes. Both sides are
/// calibrated on the same questions, so their metrics can be compared.
/// </summary>
public sealed class ShadowSide
{
    internal ShadowSide(
        IReadOnlyList<ModelCount> models,
        ErrorSummary errors,
        int refusals,
        LatencySummary latency,
        CostSummary cost,
        int verbalizedAnswers,
        MetricsCalculator.KindSet calibration,
        int calibratedDecisions,
        int calibratedKeys)
    {
        Models = models;
        Errors = errors;
        Refusals = refusals;
        Latency = latency;
        Cost = cost;
        VerbalizedAnswers = verbalizedAnswers;
        Noul = calibration.Noul;
        Choice = calibration.Choice;
        Score = calibration.Score;
        Overall = calibration.Overall;
        CalibratedDecisions = calibratedDecisions;
        CalibratedKeys = calibratedKeys;
    }

    /// <summary>
    /// Gets the models that served this side's responses, successful and <c>InvalidResponse</c>, with their counts, most
    /// common first, then by model.
    /// </summary>
    public IReadOnlyList<ModelCount> Models { get; }

    /// <summary>Gets this side's failed calls, by error kind.</summary>
    public ErrorSummary Errors { get; }

    /// <summary>Gets the number of questions this side refused, summed over the decisions.</summary>
    public int Refusals { get; }

    /// <summary>Gets the latency of this side's successful calls that were not cached.</summary>
    public LatencySummary Latency { get; }

    /// <summary>
    /// Gets the cost of this side's served calls: the reported cost, or both token counts times a matching price, as in
    /// <see cref="CalibrationReport.Cost"/>.
    /// </summary>
    public CostSummary Cost { get; }

    /// <summary>
    /// Gets the number of this side's calibrated answers whose probabilities are <see cref="ProbabilitySource.Verbalized"/>:
    /// the model stated them in its text, so they are not a measured distribution. The calibration metrics include these
    /// answers, as <see cref="CalibrationReport.VerbalizedAnswers"/> counts them.
    /// </summary>
    public int VerbalizedAnswers { get; }

    /// <summary>Gets the calibration of the Noul questions, or <see langword="null"/> when none is calibrated.</summary>
    public KindMetrics? Noul { get; }

    /// <summary>Gets the calibration of the Choice questions, or <see langword="null"/> when none is calibrated.</summary>
    public KindMetrics? Choice { get; }

    /// <summary>Gets the calibration of the Score questions, or <see langword="null"/> when none is calibrated.</summary>
    public KindMetrics? Score { get; }

    /// <summary>Gets the calibration of all calibrated questions together.</summary>
    public KindMetrics Overall { get; }

    /// <summary>Gets the number of decisions with at least one calibrated question; the same on both sides.</summary>
    public int CalibratedDecisions { get; }

    /// <summary>
    /// Gets the number of calibrated questions: those of a shadowed decision that both sides answered and a confirmed
    /// outcome answered with an answer that fits the question. The same on both sides.
    /// </summary>
    public int CalibratedKeys { get; }
}
