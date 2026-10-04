using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>The 99th percentile of the measured iterations, in microseconds; BenchmarkDotNet has no built-in P99 column.</summary>
public sealed class P99Column : IColumn
{
    /// <inheritdoc/>
    public string Id => nameof(P99Column);

    /// <inheritdoc/>
    public string ColumnName => "P99";

    /// <inheritdoc/>
    public bool AlwaysShow => true;

    /// <inheritdoc/>
    public ColumnCategory Category => ColumnCategory.Statistics;

    /// <inheritdoc/>
    public int PriorityInCategory => 10;

    /// <inheritdoc/>
    public bool IsNumeric => true;

    /// <inheritdoc/>
    public UnitType UnitType => UnitType.Dimensionless;

    /// <inheritdoc/>
    public string Legend => "Percentile 99 of the measured iterations, in microseconds";

    /// <summary>Gets the 99th percentile of <paramref name="report"/>'s measured iterations, in nanoseconds.</summary>
    /// <param name="report">A benchmark's report.</param>
    /// <returns>The percentile, or <see langword="null"/> when the benchmark has no results.</returns>
    public static double? Nanoseconds(BenchmarkReport report) => report?.ResultStatistics?.Percentiles.Percentile(99);

    /// <inheritdoc/>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, SummaryStyle.Default);

    /// <inheritdoc/>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var report = summary[benchmarkCase];
        var nanoseconds = report is null ? null : Nanoseconds(report);
        return nanoseconds is { } value
            ? (value / 1000).ToString("N1", CultureInfo.InvariantCulture) + " us"
            : "NA";
    }

    /// <inheritdoc/>
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    /// <inheritdoc/>
    public bool IsAvailable(Summary summary) => true;
}
