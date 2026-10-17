namespace Minos.Evaluation;

/// <summary>The latency of the successful requests that were not answered from a cache, by nearest rank.</summary>
public sealed class LatencySummary
{
    internal LatencySummary(int requests, double? medianMs, double? p95Ms)
    {
        Requests = requests;
        MedianMs = medianMs;
        P95Ms = p95Ms;
    }

    /// <summary>Gets the number of requests measured.</summary>
    public int Requests { get; }

    /// <summary>Gets the median, in milliseconds, or <see langword="null"/> when none is measured.</summary>
    public double? MedianMs { get; }

    /// <summary>Gets the 95th percentile, in milliseconds, or <see langword="null"/> when none is measured.</summary>
    public double? P95Ms { get; }
}
