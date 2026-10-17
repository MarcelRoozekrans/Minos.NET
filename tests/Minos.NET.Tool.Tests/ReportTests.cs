using System.Text.Json.Nodes;
using static Minos.Tool.Tests.Harness;

namespace Minos.Tool.Tests;

[Collection(ProcessEnvironment.Name)]
public sealed class ReportTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    private string[] Calibrate(string dataset, string outDir, params string[] more)
        => ["calibrate", "--dataset", dataset, "--config", _h.Config(), "--out", outDir, .. more];

    [Fact]
    public async Task Report_RebuildsTheSameReportJson()
    {
        var dataset = _h.Dataset();
        var calibrated = await Invoke(new FakeSystemOne(), Calibrate(dataset, _h.Out));
        Assert.Equal(0, calibrated.Exit);
        var again = Path.Combine(_h.Dir, "again");

        var run = await Invoke(null, "report", "--results", Path.Combine(_h.Out, "results.jsonl"), "--dataset", dataset, "--out", again);

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Stderr);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_h.Out, "report.json")), File.ReadAllBytes(Path.Combine(again, "report.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(_h.Out, "report.md")), File.ReadAllBytes(Path.Combine(again, "report.md")));
        Assert.Contains(Path.Combine(again, "report.json"), run.Stdout, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(again, "report.md"), run.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_DatasetMismatch_ExitsTwo()
    {
        await Invoke(new FakeSystemOne(), Calibrate(_h.Dataset(), _h.Out));
        var other = _h.Dataset(3, "other.jsonl", "x");
        var again = Path.Combine(_h.Dir, "again");

        var run = await Invoke(null, "report", "--results", Path.Combine(_h.Out, "results.jsonl"), "--dataset", other, "--out", again);

        Assert.Equal(2, run.Exit);
        Assert.StartsWith("line ", run.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(again));
    }

    [Fact]
    public async Task Report_FromPartialResults_IsPartial()
    {
        var dataset = _h.Dataset(5);
        var stopped = await Invoke(new FakeSystemOne { ThrowFromRequest = 3 }, Calibrate(dataset, _h.Out));
        Assert.Equal(1, stopped.Exit);
        var again = Path.Combine(_h.Dir, "again");

        var run = await Invoke(null, "report", "--results", Path.Combine(_h.Out, "results.partial.jsonl"), "--dataset", dataset, "--out", again);

        Assert.Equal(0, run.Exit);
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(again, "report.json")))!;
        Assert.True(report["header"]!["partial"]!.GetValue<bool>());
        Assert.Equal(5, report["datasetItemCount"]!.GetValue<int>());
        Assert.Contains("Partial run", File.ReadAllText(Path.Combine(again, "report.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_MalformedResults_PrintsLineNumberedProblems_ExitsTwo()
    {
        var results = _h.Write("bad.jsonl", "not json\n");

        var run = await Invoke(null, "report", "--results", results, "--dataset", _h.Dataset(), "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.StartsWith("line 1: ", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_MissingFiles_ExitTwo_Quietly()
    {
        var run = await Invoke(null, "report", "--results", Path.Combine(_h.Dir, "nope.jsonl"), "--dataset", _h.Dataset(), "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.Contains("nope.jsonl", run.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_h.Out));
    }

    [Fact]
    public async Task Report_BadPricesFile_ExitsTwo()
    {
        await Invoke(new FakeSystemOne(), Calibrate(_h.Dataset(), _h.Out));
        var prices = _h.Write("prices.json", "{\"prices\":[{\"model\":\"\"}]}");

        var run = await Invoke(null, "report", "--results", Path.Combine(_h.Out, "results.jsonl"), "--dataset", _h.Dataset(), "--prices", prices, "--out", Path.Combine(_h.Dir, "again"));

        Assert.Equal(2, run.Exit);
    }

    [Fact]
    public async Task Report_DefaultOut_IsTheHeaderProviderAndUtcTime()
    {
        var dataset = _h.Dataset();
        await Invoke(new FakeSystemOne(), Calibrate(dataset, _h.Out));
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_h.Dir);
        try
        {
            var run = await Invoke(null, "report", "--results", Path.Combine(_h.Out, "results.jsonl"), "--dataset", dataset);

            Assert.Equal(0, run.Exit);
            var made = Assert.Single(Directory.GetDirectories(Path.Combine(_h.Dir, "minos-out")));
            Assert.Matches(@"^custom-\d{8}-\d{6}$", Path.GetFileName(made));
            Assert.True(File.Exists(Path.Combine(made, "report.json")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b\\c")]
    [InlineData("")]
    [InlineData("..")]
    public void SafeFileName_KeepsOneSegment(string name)
    {
        var safe = CommandSupport.SafeFileName(name);

        Assert.DoesNotContain("..", safe, StringComparison.Ordinal);
        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain('\\', safe);
        Assert.NotEmpty(safe);
    }

    [Fact]
    public async Task Report_ProviderNameWithPathParts_StaysInsideMinosOut()
    {
        var dataset = _h.Dataset();
        await Invoke(new FakeSystemOne(), Calibrate(dataset, _h.Out));
        var results = Path.Combine(_h.Out, "results.jsonl");
        var edited = File.ReadAllText(results).Replace("\"custom\"", "\"../x\"", StringComparison.Ordinal);
        Assert.Contains("../x", edited, StringComparison.Ordinal);
        File.WriteAllText(results, edited);
        var previous = Directory.GetCurrentDirectory();
        var work = Directory.CreateDirectory(Path.Combine(_h.Dir, "work")).FullName;
        Directory.SetCurrentDirectory(work);
        try
        {
            var run = await Invoke(null, "report", "--results", results, "--dataset", dataset);

            Assert.Equal(0, run.Exit);
            var made = Assert.Single(Directory.GetDirectories(Path.Combine(work, "minos-out")));
            Assert.StartsWith("--x-", Path.GetFileName(made), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(work, "x")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}
