namespace Minos.Evaluation;

/// <summary>
/// The cost of the successful requests. An item's cost is the one its provider reported; otherwise both token counts
/// times a matching price; otherwise it is unknown. Cached responses are left out and counted separately.
/// </summary>
public sealed class CostSummary
{
    internal CostSummary(double? perThousandDecisions, int reported, int priced, int unknown, int cached, IReadOnlyList<ModelPrice> pricesUsed)
    {
        PerThousandDecisions = perThousandDecisions;
        Reported = reported;
        Priced = priced;
        Unknown = unknown;
        Cached = cached;
        PricesUsed = pricesUsed;
    }

    /// <summary>Gets the mean known cost times 1,000, or <see langword="null"/> when no cost is known.</summary>
    public double? PerThousandDecisions { get; }

    /// <summary>Gets the number of items whose provider reported a cost.</summary>
    public int Reported { get; }

    /// <summary>Gets the number of items whose cost was computed from a price.</summary>
    public int Priced { get; }

    /// <summary>Gets the number of items with no known cost.</summary>
    public int Unknown { get; }

    /// <summary>Gets the number of items answered from a cache.</summary>
    public int Cached { get; }

    /// <summary>Gets the prices used, sorted by model.</summary>
    public IReadOnlyList<ModelPrice> PricesUsed { get; }
}
