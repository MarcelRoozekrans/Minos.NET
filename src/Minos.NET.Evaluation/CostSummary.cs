namespace Minos.Evaluation;

/// <summary>
/// The cost of the successful requests. An item's cost is the one its provider reported; otherwise both token counts
/// times a matching price; otherwise it is unknown. Cached responses are left out and counted separately.
/// </summary>
/// <param name="PerThousandDecisions">The mean known cost times 1,000, or <see langword="null"/> when no cost is known.</param>
/// <param name="Reported">The number of items whose provider reported a cost.</param>
/// <param name="Priced">The number of items whose cost was computed from a price.</param>
/// <param name="Unknown">The number of items with no known cost.</param>
/// <param name="Cached">The number of items answered from a cache.</param>
/// <param name="PricesUsed">The prices used, sorted by model.</param>
public sealed record CostSummary(
    double? PerThousandDecisions,
    int Reported,
    int Priced,
    int Unknown,
    int Cached,
    IReadOnlyList<ModelPrice> PricesUsed);
