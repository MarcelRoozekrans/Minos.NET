using ZeroAlloc.Jev.Transport;

namespace ZeroAlloc.Jev.Tests;

public sealed class JevClientOptionsValidateTests
{
    [Theory]
    [MemberData(nameof(InvalidOptionsCases.All), MemberType = typeof(InvalidOptionsCases))]
    public void Validate_ThrowsWhatTheSettingsResolverThrows(string name)
    {
        var invalid = InvalidOptionsCases.Get(name);

        var expected = Record.Exception(() => JevClientSettings.Resolve(invalid.Options(), invalid.Lookup));
        var actual = Record.Exception(() => invalid.Options().Validate(invalid.Lookup));

        AssertSameException(expected, actual);
    }

    [Theory]
    [MemberData(nameof(InvalidOptionsCases.OptionsOnly), MemberType = typeof(InvalidOptionsCases))]
    public void Validate_ThrowsWhatTheConstructorThrows(string name)
    {
        var invalid = InvalidOptionsCases.Get(name);

        var expected = Record.Exception(() => new JevClient(invalid.Options()).Dispose());
        var actual = Record.Exception(() => invalid.Options().Validate());

        AssertSameException(expected, actual);
    }

    [Fact]
    public void Validate_AcceptsValidOptions()
    {
        // The key and the base address are both set, so the process environment is never read.
        var options = new JevClientOptions { ApiKey = "k", BaseAddress = new Uri("http://localhost/") };

        Assert.Null(Record.Exception(options.Validate));
    }

    [Fact]
    public void Validate_AcceptsAKeyFromTheEnvironment()
        => Assert.Null(Record.Exception(
            () => new JevClientOptions().Validate(name => string.Equals(name, "TYPESAFE_API_KEY", StringComparison.Ordinal) ? "env-key" : null)));

    [Fact]
    public void Validate_LeavesTheOptionsUnchanged()
    {
        var options = new JevClientOptions { ApiKey = " k ", Model = "  jev-1.13.0\n" };

        options.Validate(_ => null);

        Assert.Equal(" k ", options.ApiKey);
        Assert.Equal("  jev-1.13.0\n", options.Model);
        Assert.Null(options.BaseAddress);
    }

    private static void AssertSameException(Exception? expected, Exception? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.IsType(expected.GetType(), actual);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal((expected as ArgumentException)?.ParamName, (actual as ArgumentException)?.ParamName);
    }
}
