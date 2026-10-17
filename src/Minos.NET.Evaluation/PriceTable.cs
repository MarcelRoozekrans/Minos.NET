using System.Diagnostics.CodeAnalysis;

namespace Minos.Evaluation;

/// <summary>A table of model prices, used for the cost of a response whose provider reports none.</summary>
public sealed class PriceTable
{
    private readonly ModelPrice[] _prices;

    /// <summary>Creates a price table.</summary>
    /// <param name="prices">The prices.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prices"/> or one of its entries is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An entry has a null or empty model or source, or a price that is negative or not finite; or two entries have the
    /// same model.
    /// </exception>
    public PriceTable(IEnumerable<ModelPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        _prices = [.. prices];
        var models = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _prices.Length; i++)
        {
            var price = _prices[i];
            ArgumentNullException.ThrowIfNull(price, nameof(prices));
            if (string.IsNullOrEmpty(price.Model))
            {
                throw new ArgumentException(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The price at index {i} has no model."), nameof(prices));
            }

            if (string.IsNullOrEmpty(price.Source))
            {
                throw new ArgumentException($"The price of '{price.Model}' has no source.", nameof(prices));
            }

            if (!ResultsRules.IsNonNegative(price.InputPerMillion) || !ResultsRules.IsNonNegative(price.OutputPerMillion))
            {
                throw new ArgumentException($"The price of '{price.Model}' is negative or not a finite number.", nameof(prices));
            }

            if (!models.Add(price.Model))
            {
                throw new ArgumentException($"Two prices are for the model '{price.Model}'.", nameof(prices));
            }
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
