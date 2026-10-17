namespace Minos.Evaluation.Tests;

public sealed class PriceTableTests
{
    private static readonly DateOnly Date = new(2026, 10, 17);

    private static ModelPrice Price(string model) => ReportFixture.Price(model, 1, 2, Date, "test");

    [Fact]
    public void ExactMatch()
    {
        var table = new PriceTable([Price("jev-1.14"), Price("other")]);

        Assert.True(table.TryFind("jev-1.14", out var price));
        Assert.Equal("jev-1.14", price.Model);
    }

    [Fact]
    public void DatedSuffix_Matches()
    {
        var table = new PriceTable([Price("jev-1.14")]);

        Assert.True(table.TryFind("jev-1.14-20261014", out var price));
        Assert.Equal("jev-1.14", price.Model);
    }

    [Fact]
    public void LongestPrefix_Wins()
    {
        var table = new PriceTable([Price("model-a-1.14"), Price("model-a-1"), Price("model-a")]);

        // model-a-1 is a near miss: a '.' follows it, not a '-'.
        Assert.True(table.TryFind("model-a-1.14-20261014", out var price));
        Assert.Equal("model-a-1.14", price.Model);
    }

    [Fact]
    public void ExactMatch_WinsOverPrefix()
    {
        var table = new PriceTable([Price("model-a-1.14"), Price("model-a")]);

        Assert.True(table.TryFind("model-a-1.14", out var price));
        Assert.Equal("model-a-1.14", price.Model);
    }

    [Fact]
    public void LongerVersion_DoesNotMatchShorterEntry()
    {
        var table = new PriceTable([Price("jev-1.14")]);

        Assert.False(table.TryFind("jev-1.140", out var price));
        Assert.Null(price);
    }

    [Fact]
    public void NoMatch()
    {
        var table = new PriceTable([Price("jev-1.14")]);

        Assert.False(table.TryFind("MODEL-A-1.14", out _));
        Assert.False(table.TryFind("gpt-x", out _));
        Assert.False(new PriceTable([]).TryFind("jev-1.14", out _));
    }

    [Fact]
    public void Prices_KeepsTheEntries()
    {
        ModelPrice[] prices = [Price("a"), Price("b")];

        Assert.Equal(prices, new PriceTable(prices).Prices);
    }

    [Fact]
    public void InvalidEntry_Throws()
    {
        ModelPrice[] invalid =
        [
            ReportFixture.Price(null!, 1, 2, Date, "s"),
            ReportFixture.Price(string.Empty, 1, 2, Date, "s"),
            ReportFixture.Price("m", 1, 2, Date, null!),
            ReportFixture.Price("m", 1, 2, Date, string.Empty),
            ReportFixture.Price("m", -1, 2, Date, "s"),
            ReportFixture.Price("m", 1, -0.001, Date, "s"),
            ReportFixture.Price("m", double.NaN, 2, Date, "s"),
            ReportFixture.Price("m", 1, double.PositiveInfinity, Date, "s"),
        ];

        foreach (var price in invalid)
        {
            var ex = Assert.Throws<ArgumentException>(() => new PriceTable([Price("ok"), price]));
            Assert.Equal("prices", ex.ParamName);
        }
    }

    [Fact]
    public void InvalidEntry_IsNamed()
    {
        Assert.Contains("index 1", Assert.Throws<ArgumentException>(() => new PriceTable([Price("ok"), ReportFixture.Price(string.Empty, 1, 2, Date, "s")])).Message, StringComparison.Ordinal);
        Assert.Contains("'m'", Assert.Throws<ArgumentException>(() => new PriceTable([ReportFixture.Price("m", -1, 2, Date, "s")])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateModel_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new PriceTable([Price("jev-1.14"), Price("other"), Price("jev-1.14")]));

        Assert.Contains("'jev-1.14'", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, new PriceTable([Price("jev-1.14"), Price("MODEL-A-1.14")]).Prices.Count);
    }

    [Fact]
    public void FreePrice_IsAllowed()
    {
        Assert.Single(new PriceTable([ReportFixture.Price("m", 0, 0, Date, "s")]).Prices);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new PriceTable(null!));
        Assert.Throws<ArgumentNullException>(() => new PriceTable([null!]));
        Assert.Throws<ArgumentNullException>(() => new PriceTable([]).TryFind(null!, out _));
    }
}
