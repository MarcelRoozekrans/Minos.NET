using System.Diagnostics.CodeAnalysis;

namespace Minos.Evaluation;

/// <summary>A table of model prices, used for the cost of a response whose provider reports none.</summary>
public sealed class PriceTable
{
    private readonly ModelPrice[] _prices;

    /// <summary>Creates a price table.</summary>
    /// <param name="prices">The prices.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prices"/> or one of its entries is <see langword="null"/>.</exception>
    public PriceTable(IEnumerable<ModelPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        _prices = [.. prices];
        foreach (var price in _prices)
        {
            ArgumentNullException.ThrowIfNull(price, nameof(prices));
        }
    }

    /// <summary>Gets the prices, in the order given.</summary>
    public IReadOnlyList<ModelPrice> Prices => _prices;

    /// <summary>
    /// Finds the price of a model: an entry whose model equals it, or else the longest entry that it starts with,
    /// followed by <c>-</c>. Model ids are compared ordinally.
    /// </summary>
    /// <param name="model">The response's model.</param>
    /// <param name="price">The price found, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a price matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public bool TryFind(string model, [NotNullWhen(true)] out ModelPrice? price)
    {
        ArgumentNullException.ThrowIfNull(model);
        price = null;
        foreach (var entry in _prices)
        {
            if (string.Equals(model, entry.Model, StringComparison.Ordinal))
            {
                price = entry;
                return true;
            }

            if (model.Length > entry.Model.Length
                && model[entry.Model.Length] == '-'
                && model.StartsWith(entry.Model, StringComparison.Ordinal)
                && (price is null || entry.Model.Length > price.Model.Length))
            {
                price = entry;
            }
        }

        return price is not null;
    }
}
