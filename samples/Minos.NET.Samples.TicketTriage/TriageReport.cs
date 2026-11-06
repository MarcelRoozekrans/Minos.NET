using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>A value for the Choice questions, the Noul questions and all questions together.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Choice">The value over the Choice questions.</param>
/// <param name="Noul">The value over the Noul questions.</param>
/// <param name="All">The value over all questions.</param>
public sealed record PerKind<T>(T Choice, T Noul, T All);

/// <summary>A count of right answers out of the answers given.</summary>
/// <param name="Correct">The right answers.</param>
/// <param name="Total">The answers.</param>
public sealed record Hits(int Correct, int Total)
{
    /// <summary>Gets the share of right answers; <see langword="null"/> when there are no answers.</summary>
    public double? Rate => Total == 0 ? null : (double)Correct / Total;
}

/// <summary>The items read and what the run did to them.</summary>
/// <param name="ItemsRead">The record lines read.</param>
/// <param name="Decided">The items that were answered.</param>
/// <param name="FailuresByError">The failed items per error kind name.</param>
/// <param name="ServedModels">The calls per served model name.</param>
public sealed record RunSection(
    int ItemsRead,
    int Decided,
    IReadOnlyDictionary<string, int> FailuresByError,
    IReadOnlyDictionary<string, int> ServedModels);

/// <summary>The questions per route reason, and the tickets whose path had a target call.</summary>
/// <param name="Confident">The questions the local answer met its threshold for.</param>
/// <param name="Escalated">The questions the target answered.</param>
/// <param name="NoTimeLeft">The unsure questions kept on the local answer for lack of time.</param>
/// <param name="TargetFailed">The unsure questions kept on the local answer because the target failed.</param>
/// <param name="TicketsWithTargetCall">
/// The tickets with a target call that was not skipped. For All it is read from the calls; for a kind it counts the tickets
/// with an Escalated or TargetFailed question of that kind.
/// </param>
public sealed record SplitCounts(int Confident, int Escalated, int NoTimeLeft, int TargetFailed, int TicketsWithTargetCall);

/// <summary>How right the local answers, the final answers and the target answers were.</summary>
/// <param name="LocalAlone">The local answer of every question.</param>
/// <param name="Final">The final answer of every question.</param>
/// <param name="Target">The final answers of the Escalated questions, which the target gave.</param>
public sealed record AccuracySection(Hits LocalAlone, Hits Final, Hits Target);

/// <summary>The Escalated questions by whether the local and the final answer were right.</summary>
/// <param name="Fixed">The local answer was wrong and the final one right.</param>
/// <param name="Broken">The local answer was right and the final one wrong.</param>
/// <param name="BothRight">Both were right.</param>
/// <param name="BothWrong">Both were wrong.</param>
public sealed record EscalatedTable(int Fixed, int Broken, int BothRight, int BothWrong);

/// <summary>Whether the below-threshold flag found the local model's mistakes.</summary>
/// <param name="WrongCaught">The wrong local answers that were below the threshold.</param>
/// <param name="WrongMissed">The wrong local answers that met the threshold.</param>
/// <param name="BelowThreshold">The local answers below the threshold.</param>
/// <param name="BelowThresholdLocalRight">Of those, the local answers that were right.</param>
public sealed record SignalSection(int WrongCaught, int WrongMissed, int BelowThreshold, int BelowThresholdLocalRight);

/// <summary>A count of timings with their median and 95th percentile, nearest-rank, in milliseconds.</summary>
/// <param name="Count">The timings.</param>
/// <param name="Median">The median; <see langword="null"/> when there are none.</param>
/// <param name="P95">The 95th percentile; <see langword="null"/> when there are none.</param>
public sealed record TimeSummary(int Count, double? Median, double? P95);

/// <summary>Per-ticket time with and without a target call, and the time of the local and the target call.</summary>
/// <param name="WithTarget">The tickets whose path had a target call that was not skipped.</param>
/// <param name="WithoutTarget">The other decided tickets.</param>
/// <param name="Local">The local calls that reported a time.</param>
/// <param name="Target">The target calls that were made and reported a time.</param>
public sealed record LatencySection(TimeSummary WithTarget, TimeSummary WithoutTarget, TimeSummary Local, TimeSummary Target);

/// <summary>What the decided tickets cost. A ticket with any call of unknown cost is counted as unknown.</summary>
/// <param name="PerThousandTickets">The known total divided by the decided tickets, times 1000; <see langword="null"/> when no ticket has a known cost.</param>
/// <param name="Reported">The tickets whose calls all reported a cost.</param>
/// <param name="Priced">The tickets with a call costed from tokens and a price, and none unknown.</param>
/// <param name="Unknown">The tickets with a call that has neither a cost nor a price.</param>
/// <param name="PricesUsed">The prices applied, sorted by model ordinal.</param>
public sealed record CostSection(double? PerThousandTickets, int Reported, int Priced, int Unknown, IReadOnlyList<ModelPrice> PricesUsed);

/// <summary>The sections of the triage report. It holds no message text.</summary>
public sealed class TriageReport
{
    internal TriageReport(
        RunSection run,
        PerKind<SplitCounts> split,
        PerKind<AccuracySection> accuracy,
        PerKind<EscalatedTable> escalated,
        PerKind<SignalSection> signal,
        LatencySection latency,
        CostSection cost)
    {
        Run = run;
        Split = split;
        Accuracy = accuracy;
        Escalated = escalated;
        Signal = signal;
        Latency = latency;
        Cost = cost;
    }

    /// <summary>Gets the items read, decided, failed and the served models.</summary>
    public RunSection Run { get; }

    /// <summary>Gets the route reasons per kind.</summary>
    public PerKind<SplitCounts> Split { get; }

    /// <summary>Gets the accuracy per kind.</summary>
    public PerKind<AccuracySection> Accuracy { get; }

    /// <summary>Gets the two-by-two of the Escalated questions per kind.</summary>
    public PerKind<EscalatedTable> Escalated { get; }

    /// <summary>Gets the signal check per kind.</summary>
    public PerKind<SignalSection> Signal { get; }

    /// <summary>Gets the per-ticket and per-call time.</summary>
    public LatencySection Latency { get; }

    /// <summary>Gets the cost per 1000 tickets.</summary>
    public CostSection Cost { get; }
}
