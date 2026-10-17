namespace Minos.Evaluation;

/// <summary>The latency of the successful requests that were not answered from a cache, by nearest rank.</summary>
/// <param name="Requests">The number of requests measured.</param>
/// <param name="MedianMs">The median, in milliseconds, or <see langword="null"/> when none is measured.</param>
/// <param name="P95Ms">The 95th percentile, in milliseconds, or <see langword="null"/> when none is measured.</param>
public sealed record LatencySummary(int Requests, double? MedianMs, double? P95Ms);
