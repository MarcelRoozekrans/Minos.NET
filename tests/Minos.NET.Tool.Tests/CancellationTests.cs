using static Minos.Tool.Tests.Harness;

namespace Minos.Tool.Tests;

public sealed class CancellationTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Cancelled_ExitsOneThirty_NoResultsFileWritten()
    {
        using var cts = new CancellationTokenSource();
        var fake = new FakeSystemOne { Gate = async () => await cts.CancelAsync() };

        var run = await Invoke(
            fake,
            cts.Token,
            "calibrate", "--dataset", _h.Dataset(4), "--config", _h.Config(), "--out", _h.Out);

        Assert.Equal(130, run.Exit);
        Assert.Equal("Cancelled.", run.Stderr.Trim());
        Assert.False(Directory.Exists(_h.Out) && Directory.EnumerateFileSystemEntries(_h.Out).Any());
    }

    [Fact]
    public async Task Cancelled_Calibrate_BeforeStart_WritesNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var fake = new FakeSystemOne();

        var run = await Invoke(fake, cts.Token, "calibrate", "--dataset", _h.Dataset(), "--config", _h.Config(), "--out", _h.Out);

        Assert.Equal(130, run.Exit);
        Assert.Equal(0, fake.Requests);
        Assert.False(Directory.Exists(_h.Out));
    }

    [Fact]
    public async Task Cancelled_Report_ExitsOneThirty_WritesNothing()
    {
        var dataset = _h.Dataset();
        await Invoke(new FakeSystemOne(), "calibrate", "--dataset", dataset, "--config", _h.Config(), "--out", _h.Out);
        var again = Path.Combine(_h.Dir, "again");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var run = await Invoke(null, cts.Token, "report", "--results", Path.Combine(_h.Out, "results.jsonl"), "--dataset", dataset, "--out", again);

        Assert.Equal(130, run.Exit);
        Assert.False(Directory.Exists(again));
    }

    [Fact]
    public async Task Cancelled_Conformance_ExitsOneThirty_WritesNothing()
    {
        using var cts = new CancellationTokenSource();
        var fake = new FakeSystemOne { Gate = async () => await cts.CancelAsync() };

        var run = await Invoke(
            fake,
            cts.Token,
            "conformance", "--config", _h.Config("\"Provider\":\"TypeSafe\",\"ApiKey\":\"k\",\"Model\":\"jev-latest\""), "--out", _h.Out);

        Assert.Equal(130, run.Exit);

        // The directory is reserved before the first request, so it exists, but no report is written into it.
        Assert.Empty(Directory.GetFileSystemEntries(_h.Out));
    }

    [Fact]
    public async Task Cancelled_Conformance_BeforeStart_SendsNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var fake = new FakeSystemOne();

        var run = await Invoke(fake, cts.Token, "conformance", "--config", _h.Config(), "--out", _h.Out);

        Assert.Equal(130, run.Exit);
        Assert.Equal(0, fake.Requests);
        Assert.False(Directory.Exists(_h.Out));
    }
}
