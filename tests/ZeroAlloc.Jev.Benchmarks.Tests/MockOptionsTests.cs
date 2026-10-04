using ZeroAlloc.Jev.Benchmarks.Mock;

namespace ZeroAlloc.Jev.Benchmarks.Tests;

/// <summary>The mock's command line, which rejects a missing or bad value the way the harness's does.</summary>
public sealed class MockOptionsTests
{
    [Fact]
    public void No_arguments_listen_on_5005_on_every_core()
    {
        Assert.Equal(new MockOptions(MockOptions.DefaultPort, null), MockOptions.Parse([]));
        Assert.Equal(5005, MockOptions.DefaultPort);
    }

    [Fact]
    public void Port_and_cores_are_read()
    {
        Assert.Equal(new MockOptions(6000, 0xF0UL), MockOptions.Parse(["--port", "6000", "--cores", "4-7"]));
    }

    [Fact]
    public void Port_zero_picks_a_free_port()
    {
        Assert.Equal(0, MockOptions.Parse(["--port", "0"]).Port);
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("--cores")]
    [InlineData("--port", "6000", "--cores")]
    [InlineData("--port", "abc")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "65536")]
    [InlineData("--port", "")]
    [InlineData("--cores", "0x0")]
    [InlineData("--cores", "x")]
    [InlineData("--fast")]
    public void A_missing_or_bad_value_is_rejected(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => MockOptions.Parse(args));
    }
}
