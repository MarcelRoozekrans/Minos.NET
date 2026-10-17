namespace Minos.Evaluation.Tests;

public sealed class PriceTableTests
{
    private static readonly DateOnly Date = new(2026, 10, 17);

    private static ModelPrice Price(string model) => new(model, 1, 2, Date, "test");

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
        var table = new PriceTable([Price("jev"), Price("jev-1.14"), Price("jev-1")]);

        Assert.True(table.TryFind("jev-1.14-20261014", out var price));
        Assert.Equal("jev-1.14", price.Model);
    }

    [Fact]
    public void ExactMatch_WinsOverPrefix()
    {
        var table = new PriceTable([Price("jev"), Price("jev-1.14-20261014")]);

        Assert.True(table.TryFind("jev-1.14-20261014", out var price));
        Assert.Equal("jev-1.14-20261014", price.Model);
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

        Assert.False(table.TryFind("JEV-1.14", out _));
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
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new PriceTable(null!));
        Assert.Throws<ArgumentNullException>(() => new PriceTable([null!]));
        Assert.Throws<ArgumentNullException>(() => new PriceTable([]).TryFind(null!, out _));
    }
}
