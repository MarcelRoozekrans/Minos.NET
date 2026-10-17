using ZeroAlloc.Results;

namespace Minos.Evaluation;

/// <summary>
/// The calibration report of a run: accuracy, calibration error, reliability bins and a threshold sweep per question
/// kind and overall, with latency, cost and errors. It is computed from saved results and the dataset they ran.
/// </summary>
public sealed class CalibrationReport
{
    internal CalibrationReport(
        RunHeader header,
        KindMetrics? noul,
        KindMetrics? choice,
        KindMetrics? score,
        KindMetrics overall,
        int unscoredQuestions,
        LatencySummary latency,
        CostSummary cost,
        ErrorSummary errors,
        IReadOnlyList<ModelCount> responseModels)
    {
        Header = header;
        Noul = noul;
        Choice = choice;
        Score = score;
        Overall = overall;
        UnscoredQuestions = unscoredQuestions;
        Latency = latency;
        Cost = cost;
        Errors = errors;
        ResponseModels = responseModels;
    }

    /// <summary>Gets what the run was.</summary>
    public RunHeader Header { get; }

    /// <summary>Gets the metrics of the Noul questions, or <see langword="null"/> when none is scored.</summary>
    public KindMetrics? Noul { get; }

    /// <summary>Gets the metrics of the Choice questions, or <see langword="null"/> when none is scored.</summary>
    public KindMetrics? Choice { get; }

    /// <summary>Gets the metrics of the Score questions, or <see langword="null"/> when none is scored.</summary>
    public KindMetrics? Score { get; }

    /// <summary>Gets the metrics of all scored questions together.</summary>
    public KindMetrics Overall { get; }

    /// <summary>Gets the number of questions that were asked but have no expected answer, so are not scored.</summary>
    public int UnscoredQuestions { get; }

    /// <summary>Gets the latency of the successful requests that were not cached.</summary>
    public LatencySummary Latency { get; }

    /// <summary>Gets the cost of the successful requests.</summary>
    public CostSummary Cost { get; }

    /// <summary>Gets the failed requests.</summary>
    public ErrorSummary Errors { get; }

    /// <summary>Gets the models of the successful responses with their counts, most common first, then by model.</summary>
    public IReadOnlyList<ModelCount> ResponseModels { get; }

    /// <summary>Computes the report of a run against the dataset it ran.</summary>
    /// <param name="results">The run's results.</param>
    /// <param name="dataset">The dataset the run ran, for the expected answers.</param>
    /// <param name="prices">The prices for responses whose provider reports no cost, or <see langword="null"/>.</param>
    /// <returns>
    /// The report, or the problems when the results do not fit the dataset, with the 1-based item index as each problem's
    /// line: a different item count in the header or the results; a different id at some position; or a saved answer that
    /// does not fit its item, because its key is not a question of the item, its kind is not the question's, its option
    /// or level is not one of the question's, or its confidence or probability is not a number from 0 to 1. An answer
    /// that is missing is not a problem: that question counts as scored but unanswered.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> or <paramref name="dataset"/> is <see langword="null"/>.</exception>
    public static Result<CalibrationReport, DatasetProblems> Create(EvaluationResults results, DecisionDataset dataset, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(dataset);
        var problems = MetricsCalculator.Match(results, dataset);
        return problems is null
            ? Result<CalibrationReport, DatasetProblems>.Success(MetricsCalculator.Compute(results, dataset, prices))
            : Result<CalibrationReport, DatasetProblems>.Failure(problems);
    }
}
