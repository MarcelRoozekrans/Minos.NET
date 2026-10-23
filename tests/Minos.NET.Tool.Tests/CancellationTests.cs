using static Minos.Tool.Tests.Harness;

namespace Minos.Tool.Tests;

public sealed class CancellationTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    // The third request cancels the run and then waits for the cancellation, so the first two items are finished and paid.
    private static FakeSystemOne CancelsOnTheThirdRequest(CancellationTokenSource cts)
    {
        var count = 0;
        return new FakeSystemOne
        {
            Gate = async () =>
            {
                if (Interlocked.Increment(ref count) == 3)
                {
                    await cts.CancelAsync();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                }
            },
        };
    }

    [Fact]
    public async Task Cancelled_MidRun_WritesThePartialResultsAndReport_AndExitsOneThirty()
    {
        using var cts = new CancellationTokenSource();
        var fake = CancelsOnTheThirdRequest(cts);
        var dataset = _h.Dataset(5);

        var run = await Invoke(fake, cts.Token, "calibrate", "--dataset", dataset, "--config", _h.Config(), "--out", _h.Out)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(130, run.Exit);
        Assert.Equal("Cancelled.", run.Stderr.Trim());
        Assert.Equal(3, fake.Requests);
        var partial = Path.Combine(_h.Out, "results.partial.jsonl");
        var json = Path.Combine(_h.Out, "report.json");
        var markdown = Path.Combine(_h.Out, "report.md");
        Assert.Equal([partial, json, markdown], run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.False(File.Exists(Path.Combine(_h.Out, "results.jsonl")));
        var loaded = Minos.Evaluation.EvaluationResults.Load(partial);
        Assert.True(loaded.IsSuccess);
        Assert.True(loaded.Value.Header.Partial);
        Assert.Equal(["i1", "i2"], loaded.Value.Items.Select(i => i.Id));
        Assert.Contains("\"partial\"", File.ReadAllText(json), StringComparison.Ordinal);
        Assert.Contains("Partial run: 2 of 5 items", File.ReadAllText(markdown), StringComparison.Ordinal);

        // What was saved works with minos report, together with the full dataset.
        var again = Path.Combine(_h.Dir, "again");
        var report = await Invoke(null, "report", "--results", partial, "--dataset", dataset, "--out", again);
        Assert.Equal(0, report.Exit);
    }

    [Fact]
    public async Task Cancelled_MidRun_RemovesAStaleResultsFile()
    {
        Directory.CreateDirectory(_h.Out);
        File.WriteAllText(Path.Combine(_h.Out, "results.jsonl"), "stale");
        using var cts = new CancellationTokenSource();

        var run = await Invoke(CancelsOnTheThirdRequest(cts), cts.Token, "calibrate", "--dataset", _h.Dataset(5), "--config", _h.Config(), "--out", _h.Out)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(130, run.Exit);
        Assert.False(File.Exists(Path.Combine(_h.Out, "results.jsonl")));
        Assert.True(File.Exists(Path.Combine(_h.Out, "results.partial.jsonl")));
    }

    [Fact]
    public async Task Cancelled_OnTheFirstRequest_SavesAnEmptyPartialRun_AndExitsOneThirty()
    {
        using var cts = new CancellationTokenSource();
        var fake = new FakeSystemOne
        {
            Gate = async () =>
            {
                await cts.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
            },
        };

        var run = await Invoke(fake, cts.Token, "calibrate", "--dataset", _h.Dataset(4), "--config", _h.Config(), "--out", _h.Out)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(130, run.Exit);
        Assert.Equal("Cancelled.", run.Stderr.Trim());
        var loaded = Minos.Evaluation.EvaluationResults.Load(Path.Combine(_h.Out, "results.partial.jsonl"));
        Assert.True(loaded.IsSuccess);
        Assert.Empty(loaded.Value.Items);
        Assert.True(File.Exists(Path.Combine(_h.Out, "report.md")));
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
