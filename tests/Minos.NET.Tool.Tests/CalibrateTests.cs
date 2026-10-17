using System.Text.Json.Nodes;

namespace Minos.Tool.Tests;

[Collection(ProcessEnvironment.Name)]
public sealed class CalibrateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("minos-tool-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Item(string id)
        => "{\"id\":\"" + id + "\",\"state\":\"s " + id + "\",\"version\":1,\"questions\":{\"q\":{\"type\":\"noul\",\"instructions\":\"?\"}},\"expected\":{\"q\":true}}";

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Config(string providerBody = "\"Provider\":\"Custom\",\"BaseAddress\":\"http://fake.local/\",\"Model\":\"fake-1\"", string active = "Fake")
        => Write("minos.json", "{\"Minos\":{\"Active\":\"" + active + "\",\"Providers\":{\"Fake\":{" + providerBody + "}}}}");

    private string Dataset(int items = 3) => Write("data.jsonl", string.Join('\n', Enumerable.Range(1, items).Select(i => Item("i" + i))) + "\n");

    private string Out => Path.Combine(_dir, "out");

    private sealed record Run(int Exit, string Stdout, string Stderr);

    private static async Task<Run> Invoke(FakeSystemOne? fake, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await MinosCli.RunAsync(args, stdout, stderr, fake is null ? null : () => fake);
        return new Run(exit, stdout.ToString(), stderr.ToString());
    }

    private static string[] Calibrate(string dataset, string config, string outDir, params string[] more)
        => ["calibrate", "--dataset", dataset, "--config", config, "--out", outDir, .. more];

    [Fact]
    public async Task Calibrate_WritesResultsAndReports_ExitsZero()
    {
        var fake = new FakeSystemOne();
        var run = await Invoke(fake, Calibrate(Dataset(), Config(), Out));

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Stderr);
        foreach (var name in new[] { "results.jsonl", "report.json", "report.md" })
        {
            Assert.True(File.Exists(Path.Combine(Out, name)), name);
            Assert.Contains(Path.Combine(Out, name), run.Stdout, StringComparison.Ordinal);
        }

        Assert.Equal("minos-report", JsonNode.Parse(File.ReadAllText(Path.Combine(Out, "report.json")))!["kind"]!.GetValue<string>());
        Assert.Equal(3, fake.Requests);
    }

    [Fact]
    public async Task ModelOption_ReachesTheRequestBody()
    {
        var fake = new FakeSystemOne();
        var run = await Invoke(fake, Calibrate(Dataset(1), Config(), Out, "--model", "other-model"));

        Assert.Equal(0, run.Exit);
        Assert.Equal("other-model", fake.Bodies.Single()["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConfiguredModel_IsUsedWithoutTheOption()
    {
        var fake = new FakeSystemOne();
        await Invoke(fake, Calibrate(Dataset(1), Config(), Out));

        Assert.Equal("fake-1", fake.Bodies.Single()["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task ParallelOption_IsHonoured()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSystemOne? fake = null;
        fake = new FakeSystemOne
        {
            Gate = async () =>
            {
                if (fake!.PeakConcurrency >= 3)
                {
                    release.TrySetResult();
                }

                await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            },
        };

        var run = await Invoke(fake, Calibrate(Dataset(6), Config(), Out, "--parallel", "3"));

        Assert.Equal(0, run.Exit);
        Assert.Equal(3, fake.PeakConcurrency);
    }

    [Fact]
    public async Task PricesOption_GivesPricedCost()
    {
        var prices = Write("prices.json", "{\"prices\":[{\"model\":\"fake-1\",\"inputPerMillion\":1000,\"outputPerMillion\":2000,\"date\":\"2026-10-17\",\"source\":\"test\"}]}");
        var run = await Invoke(new FakeSystemOne(), Calibrate(Dataset(), Config(), Out, "--prices", prices));

        Assert.Equal(0, run.Exit);
        var json = File.ReadAllText(Path.Combine(Out, "report.json"));
        Assert.Contains("fake-1", json, StringComparison.Ordinal);
        Assert.Contains("test", json, StringComparison.Ordinal);
        Assert.Contains("priced", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BadPricesFile_ExitsTwo()
    {
        var prices = Write("prices.json", "{\"prices\":[{\"model\":\"\"}]}");
        var fake = new FakeSystemOne();
        var run = await Invoke(fake, Calibrate(Dataset(), Config(), Out, "--prices", prices));

        Assert.Equal(2, run.Exit);
        Assert.NotEqual("", run.Stderr);
        Assert.Equal(0, fake.Requests);
    }

    [Fact]
    public async Task DatasetWithProblems_ExitsTwo_PrintsLineNumbers()
    {
        var dataset = Write("bad.jsonl", Item("a") + "\nnot json\n" + Item("c") + "\n");
        var fake = new FakeSystemOne();
        var run = await Invoke(fake, Calibrate(dataset, Config(), Out));

        Assert.Equal(2, run.Exit);
        Assert.Contains("line 2: ", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, fake.Requests);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public async Task UnknownProvider_ExitsTwo_NamesTheConfiguredOnes()
    {
        var run = await Invoke(new FakeSystemOne(), Calibrate(Dataset(), Config(), Out, "--provider", "Nope"));

        Assert.Equal(2, run.Exit);
        Assert.Contains("Nope", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("Fake", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMinosSection_ExitsTwo()
    {
        var config = Write("empty.json", "{}");
        var run = await Invoke(new FakeSystemOne(), Calibrate(Dataset(), config, Out));

        Assert.Equal(2, run.Exit);
        Assert.NotEqual("", run.Stderr);
    }

    [Fact]
    public async Task MissingConfigFile_ExitsTwo()
    {
        var run = await Invoke(new FakeSystemOne(), Calibrate(Dataset(), Path.Combine(_dir, "none.json"), Out));

        Assert.Equal(2, run.Exit);
        Assert.NotEqual("", run.Stderr);
    }

    [Fact]
    public async Task InvalidOptionsSection_ExitsTwo()
    {
        var fake = new FakeSystemOne();
        var config = Config("\"Provider\":\"Custom\",\"MaxRetries\":-5");
        var run = await Invoke(fake, Calibrate(Dataset(), config, Out));

        Assert.Equal(2, run.Exit);
        Assert.NotEqual("", run.Stderr);
        Assert.Equal(0, fake.Requests);
    }

    [Fact]
    public async Task MissingOrDirectoryDatasetPath_ExitsTwo()
    {
        var config = Config();
        var missing = await Invoke(null, Calibrate(Path.Combine(_dir, "nope.jsonl"), config, Out));
        var directory = await Invoke(null, Calibrate(_dir, config, Out));

        Assert.Equal(2, missing.Exit);
        Assert.Equal(2, directory.Exit);
        Assert.NotEqual("", missing.Stderr);
        Assert.NotEqual("", directory.Stderr);
    }

    [Fact]
    public async Task NoDatasetOption_ExitsTwo()
    {
        var run = await Invoke(null, "calibrate");

        Assert.Equal(2, run.Exit);
        Assert.NotEqual("", run.Stderr);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public async Task ParallelZeroOrText_ExitsTwo_NoRequest(string parallel)
    {
        var fake = new FakeSystemOne();
        var run = await Invoke(fake, Calibrate(Dataset(), Config(), Out, "--parallel", parallel));

        Assert.Equal(2, run.Exit);
        Assert.NotEqual("", run.Stderr);
        Assert.Equal(0, fake.Requests);
    }

    [Fact]
    public async Task ClientThrowsMidRun_ExitsOne_WritesPartialResultsAndReport()
    {
        var fake = new FakeSystemOne { ThrowFromRequest = 3 };
        var run = await Invoke(fake, Calibrate(Dataset(5), Config(), Out));

        Assert.Equal(1, run.Exit);
        Assert.True(File.Exists(Path.Combine(Out, "results.partial.jsonl")));
        Assert.False(File.Exists(Path.Combine(Out, "results.jsonl")));
        Assert.True(File.Exists(Path.Combine(Out, "report.json")));
        Assert.True(File.Exists(Path.Combine(Out, "report.md")));
        Assert.Contains("\"partial\"", File.ReadAllText(Path.Combine(Out, "report.json")), StringComparison.Ordinal);
        Assert.Contains("results.partial.jsonl", run.Stdout, StringComparison.Ordinal);
        Assert.Single(run.Stderr.Trim().Split('\n'));
    }

    [Fact]
    public async Task OutDirectoryWithAPreviousRun_IsOverwritten()
    {
        var config = Config();
        var dataset = Dataset();
        Assert.Equal(0, (await Invoke(new FakeSystemOne(), Calibrate(dataset, config, Out))).Exit);
        File.WriteAllText(Path.Combine(Out, "report.md"), "stale");

        var run = await Invoke(new FakeSystemOne(), Calibrate(dataset, config, Out));

        Assert.Equal(0, run.Exit);
        Assert.NotEqual("stale", File.ReadAllText(Path.Combine(Out, "report.md")));
    }

    [Fact]
    public async Task SuccessfulRun_RemovesAStalePartialResultsFile()
    {
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "results.partial.jsonl"), "stale");

        var run = await Invoke(new FakeSystemOne(), Calibrate(Dataset(), Config(), Out));

        Assert.Equal(0, run.Exit);
        Assert.False(File.Exists(Path.Combine(Out, "results.partial.jsonl")));
        Assert.True(File.Exists(Path.Combine(Out, "results.jsonl")));
    }

    [Fact]
    public async Task PartialRun_RemovesAStaleResultsFile()
    {
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "results.jsonl"), "stale");

        var run = await Invoke(new FakeSystemOne { ThrowFromRequest = 2 }, Calibrate(Dataset(4), Config(), Out));

        Assert.Equal(1, run.Exit);
        Assert.False(File.Exists(Path.Combine(Out, "results.jsonl")));
        Assert.True(File.Exists(Path.Combine(Out, "results.partial.jsonl")));
    }

    [Fact]
    public async Task FaultWhileCancelled_Exits130()
    {
        using var cts = new CancellationTokenSource();
        var fake = new FakeSystemOne
        {
            ThrowFromRequest = 2,
            Gate = async () =>
            {
                await cts.CancelAsync();
            },
        };
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await MinosCli.RunAsync(Calibrate(Dataset(4), Config(), Out), stdout, stderr, () => fake, cts.Token);

        Assert.Equal(130, exit);
    }

    [Fact]
    public async Task MultiLineFailure_IsOneLineOnStderr()
    {
        var fake = new FakeSystemOne { ThrowFromRequest = 2, FaultMessage = "first line\r\nsecond line\nthird" };
        var run = await Invoke(fake, Calibrate(Dataset(4), Config(), Out));

        Assert.Equal(1, run.Exit);
        var line = Assert.Single(run.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("first line second line third", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultOut_IsProviderAndUtcTime_AndNeverReusesADirectory()
    {
        var when = new DateTime(2026, 10, 17, 13, 5, 9, DateTimeKind.Utc);
        var first = CalibrateCommand.NextDefaultOut(_dir, "Fake", when);
        Assert.Equal(Path.Combine(_dir, "Fake-20261017-130509"), first);

        Directory.CreateDirectory(first);
        var second = CalibrateCommand.NextDefaultOut(_dir, "Fake", when);
        Assert.Equal(first + "-2", second);

        Directory.CreateDirectory(second);
        Assert.Equal(first + "-3", CalibrateCommand.NextDefaultOut(_dir, "Fake", when));
    }

    [Fact]
    public async Task OutPathThatIsAFile_ExitsOne()
    {
        var blocker = Write("blocked", "x");
        var run = await Invoke(new FakeSystemOne(), Calibrate(Dataset(), Config(), blocker));

        Assert.Equal(1, run.Exit);
        Assert.NotEqual("", run.Stderr);
    }

    [Fact]
    public async Task Cancelled_Exits130()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await MinosCli.RunAsync(Calibrate(Dataset(), Config(), Out), stdout, stderr, () => new FakeSystemOne(), cts.Token);

        Assert.Equal(130, exit);
    }

    [Fact]
    public async Task EnvironmentVariable_OverridesTheFile()
    {
        var fake = new FakeSystemOne();
        using (new ScopedEnvironment("Minos__Providers__Fake__Model", "from-env"))
        {
            var run = await Invoke(fake, Calibrate(Dataset(1), Config(), Out));
            Assert.Equal(0, run.Exit);
        }

        Assert.Equal("from-env", fake.Bodies.Single()["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task NoArguments_ShowsHelp_ExitsZero()
    {
        var run = await Invoke(null);

        Assert.Equal(0, run.Exit);
        Assert.Contains("calibrate", run.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownCommand_ExitsTwo()
    {
        var run = await Invoke(null, "frobnicate");

        Assert.Equal(2, run.Exit);
        Assert.NotEqual("", run.Stderr);
    }

    private sealed class ScopedEnvironment : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public ScopedEnvironment(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
