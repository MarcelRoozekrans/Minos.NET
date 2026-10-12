namespace Minos.Caching.Tests;

public sealed class DecisionCacheOptionsTests
{
    [Fact]
    public void The_defaults_are_five_minutes_the_same_locally_and_the_minos_prefix()
    {
        var options = new DecisionCacheOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.Expiration);
        Assert.Null(options.LocalCacheExpiration);
        Assert.Equal("minos:", options.KeyPrefix);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Expiration_must_be_positive(int seconds)
    {
        var options = new DecisionCacheOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Expiration = TimeSpan.FromSeconds(seconds));
        Assert.Equal(TimeSpan.FromMinutes(5), options.Expiration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LocalCacheExpiration_must_be_null_or_positive(int seconds)
    {
        var options = new DecisionCacheOptions { LocalCacheExpiration = TimeSpan.FromSeconds(30) };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.LocalCacheExpiration = TimeSpan.FromSeconds(seconds));
        Assert.Equal(TimeSpan.FromSeconds(30), options.LocalCacheExpiration);
        options.LocalCacheExpiration = null;
        Assert.Null(options.LocalCacheExpiration);
    }
}
