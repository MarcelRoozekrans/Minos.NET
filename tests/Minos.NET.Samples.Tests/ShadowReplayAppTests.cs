using Minos.Samples.ShadowReplay;

namespace Minos.Samples.Tests;

/// <summary>The command line's exit codes that need no network: every problem with the invocation exits 2 and says what it is.</summary>
[Collection("Environment")]
public sealed class ShadowReplayAppTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"replay-app-{Guid.NewGuid():N}");
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public ShadowReplayAppTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<int> RunAsync(params string[] args) => ShadowReplayApp.RunAsync(args, _out, _err, CancellationToken.None);

    private string[] Full(string dataset, string config, string? records = null) =>
        ["--dataset", dataset, "--config", config, "--primary", "Local", "--candidate", "Local", "--records", records ?? Path.Combine(_dir, "records.jsonl")];

    [Fact]
    public async Task MissingOptions_ExitWithTwo_AndNameThem()
    {
        var code = await RunAsync("--dataset", "d.jsonl");

        Assert.Equal(2, code);
        Assert.Contains("--config", _err.ToString(), StringComparison.Ordinal);
        Assert.Contains("--records", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownOption_ExitsWithTwo()
    {
        var code = await RunAsync("--bogus", "x");

        Assert.Equal(2, code);
        Assert.Contains("--bogus", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableDataset_ExitsWithTwo()
    {
        var config = Write("minos.json", """{"Minos":{"Providers":{"Local":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:1/","Model":"m"}}}}""");

        var code = await RunAsync(Full(Path.Combine(_dir, "missing.jsonl"), config));

        Assert.Equal(2, code);
        Assert.Contains("dataset", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedConfig_ExitsWithTwo_AndLeavesAnEarlierRecordsFile()
    {
        var dataset = Write("d.jsonl", """{"id":"a","state":"s","questions":{"q":{"type":"noul","instructions":"Is it?","criteria":{"true":"Yes.","false":"No."}}},"expected":{"q":true},"source":"t","version":1}""");
        var config = Write("minos.json", "{ not json");
        var records = Write("records.jsonl", "earlier");

        var code = await RunAsync(Full(dataset, config, records));

        Assert.Equal(2, code);
        Assert.Contains("configuration", _err.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(records));
    }

    [Fact]
    public async Task AMissingProviderSection_ExitsWithTwo()
    {
        var dataset = Write("d.jsonl", """{"id":"a","state":"s","questions":{"q":{"type":"noul","instructions":"Is it?","criteria":{"true":"Yes.","false":"No."}}},"expected":{"q":true},"source":"t","version":1}""");
        var config = Write("minos.json", """{"Minos":{"Providers":{"Other":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:1/","Model":"m"}}}}""");

        var code = await RunAsync(Full(dataset, config));

        Assert.Equal(2, code);
        Assert.Contains("Other", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADatasetPastTheDailyCap_ExitsWithTwo_AndLeavesAnEarlierRecordsFile()
    {
        var lines = Enumerable.Range(1, ShadowReplayLoop.DailyCap + 1)
            .Select(i => $$$$"""{"id":"item-{{{{i}}}}","state":"s","questions":{"q":{"type":"noul","instructions":"Is it?","criteria":{"true":"Yes.","false":"No."}}},"expected":{"q":true},"version":1}""");
        var dataset = Write("d.jsonl", string.Join('\n', lines));
        var config = Write("minos.json", """{"Minos":{"Providers":{"Local":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:1/","Model":"m"}}}}""");
        var records = Write("records.jsonl", "earlier");

        var code = await RunAsync(Full(dataset, config, records));

        Assert.Equal(2, code);
        Assert.Contains("daily cap of 400", _err.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(records));
    }
}
