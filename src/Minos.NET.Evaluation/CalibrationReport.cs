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
        IReadOnlyList<ModelCount> responseModels,
        int datasetItemCount)
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
        DatasetItemCount = datasetItemCount;
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

    /// <summary>Gets the cost of the served requests: the successful ones and the <c>InvalidResponse</c> ones.</summary>
    public CostSummary Cost { get; }

    /// <summary>Gets the failed requests.</summary>
    public ErrorSummary Errors { get; }

    /// <summary>
    /// Gets the models of the served responses, successful and <c>InvalidResponse</c>, with their counts, most common
    /// first, then by model.
    /// </summary>
    public IReadOnlyList<ModelCount> ResponseModels { get; }

    /// <summary>Gets a value that says the run stopped early, so the report covers only the items that finished.</summary>
    public bool Partial => Header.Partial;

    /// <summary>Gets the number of items in the dataset, which exceeds the header's item count for a partial run.</summary>
    public int DatasetItemCount { get; }

    /// <summary>Writes the report as JSON: <c>"kind":"minos-report","version":1</c>, every number at full precision, an unknown number as <c>null</c>.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => ReportJsonWriter.Write(this);

    /// <summary>Writes the report as Markdown, with ratios rounded to four decimals and an unknown number as <c>n/a</c>.</summary>
    /// <returns>The Markdown text.</returns>
    public string ToMarkdown() => ReportMarkdownWriter.Write(this);

    /// <summary>Computes the report of a run against the dataset it ran.</summary>
    /// <param name="results">The run's results.</param>
    /// <param name="dataset">The dataset the run ran, for the expected answers.</param>
    /// <param name="prices">The prices for responses whose provider reports no cost, or <see langword="null"/>.</param>
    /// <returns>
    /// The report, or every problem found. The results must be ones <see cref="EvaluationResults.Save(Stream)"/> would
    /// write, such as a finite, non-negative latency and exactly one of answers and error per item; and they must fit
    /// the dataset: the same item count, the same id at each position, and every saved answer a question of its item, of
    /// the question's kind, with an option or level the question has. Results of a partial run (see
    /// <see cref="RunHeader.Partial"/>) instead must be items of the dataset, in the dataset's order, and the report
    /// covers only those. A missing answer is not a problem: that question counts as scored but unanswered. Each
    /// problem's line is the dataset line of the item it is about; a problem with the results header has line 0, and
    /// one past the dataset's last item the line after it.
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
