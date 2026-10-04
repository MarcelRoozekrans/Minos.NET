using System.Text.Json;
using ZeroAlloc.Jev.Benchmarks.Compare;
using ZeroAlloc.Jev.Benchmarks.Compare.Adapters;
using ZeroAlloc.Jev.Benchmarks.Mock;

namespace ZeroAlloc.Jev.Benchmarks.Tests;

/// <summary>The comparison harness's parts other than BenchmarkDotNet: the command line, the request count, the throughput loop and the result file.</summary>
public sealed class HarnessTests : IAsyncLifetime
{
    private static readonly string ResponsePath = Path.Combine(AppContext.BaseDirectory, "response.json");

    private MockServer _server = null!;

    private Uri BaseAddress => _server.BaseAddress;

    [Fact]
    public void Options_parse_every_argument()
    {
        var options = CompareOptions.Parse(["--base-url", "http://127.0.0.1:5005", "--out", "results", "--smoke", "--machine", "ci box"], "default");

        Assert.Equal(new Uri("http://127.0.0.1:5005"), options.BaseUrl);
        Assert.Equal("results", options.OutDirectory);
        Assert.True(options.Smoke);
        Assert.Equal("ci box", options.Machine);
        Assert.Equal("dotnet-ci-box.json", options.ResultFileName);
    }

    [Fact]
    public void Options_default_the_machine_name_and_a_full_run()
    {
        var options = CompareOptions.Parse(["--base-url", "http://127.0.0.1:5005", "--out", "results"], "HOST-1");

        Assert.False(options.Smoke);
        Assert.Equal("HOST-1", options.Machine);
        Assert.Equal("dotnet-HOST-1.json", options.ResultFileName);
    }

    [Theory]
    [InlineData("--out", "results")]
    [InlineData("--base-url", "not a url", "--out", "results")]
    [InlineData("--base-url", "ftp://127.0.0.1", "--out", "results")]
    [InlineData("--base-url", "http://127.0.0.1:5005")]
    [InlineData("--base-url", "http://127.0.0.1:5005", "--out", "results", "--machine")]
    [InlineData("--base-url", "http://127.0.0.1:5005", "--out", "results", "--fast")]
    public void Options_reject_a_wrong_command_line(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => CompareOptions.Parse(args, "default"));
    }

    [Fact]
    public async Task The_counter_reads_the_served_requests()
    {
        using var counter = new MockRequestCounter(BaseAddress);
        using var adapter = ClientAdapters.Create(ClientAdapters.Raw, BaseAddress);
        Assert.Equal(0, await counter.CountAsync(CancellationToken.None));

        _ = await adapter.CallAsync(CancellationToken.None);
        _ = await adapter.CallAsync(CancellationToken.None);

        Assert.Equal(2, await counter.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_throughput_phase_counts_every_request_the_mock_serves()
    {
        using var counter = new MockRequestCounter(BaseAddress);
        using var adapter = ClientAdapters.Create(ClientAdapters.Jev, BaseAddress);

        var phase = await ThroughputRunner.RunPhaseAsync(adapter, workers: 4, TimeSpan.FromMilliseconds(200));

        Assert.True(phase.Calls > 0);
        Assert.Equal(phase.Calls, await counter.CountAsync(CancellationToken.None));
        Assert.True(phase.PerSecond > 0);
    }

    [Fact]
    public void The_result_file_has_the_shared_format()
    {
        var file = new ResultFile(
            new MachineInfo("box", "Windows", "CPU", "2026-10-04T00:00:00Z", 30000.5),
            [
                new ClientResult("jevsharp", "JevSharp", "0.2.0", ".NET", "10.0.0", new LatencyFigures(1.5, 1.25, 3), 9000, 16, 1024) { Note = "no auth header" },
                new ClientResult("jev-net", "Jev.Net", "0.4.0", ".NET", "10.0.0", new LatencyFigures(1, 1, 2), 8000, 16, null),
            ]);

        using var json = JsonDocument.Parse(file.ToJson());
        var root = json.RootElement;

        Assert.Equal(["machine", "results"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["name", "os", "cpu", "date", "mockCeilingPerSecond"], root.GetProperty("machine").EnumerateObject().Select(p => p.Name));
        Assert.Equal(30000.5, root.GetProperty("machine").GetProperty("mockCeilingPerSecond").GetDouble());
        var first = root.GetProperty("results")[0];
        Assert.Equal(
            ["client", "library", "version", "runtime", "runtimeVersion", "latencyMs", "throughputPerSecond", "concurrency", "allocatedBytesPerCall", "note"],
            first.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["mean", "p50", "p99"], first.GetProperty("latencyMs").EnumerateObject().Select(p => p.Name));
        Assert.Equal(1.25, first.GetProperty("latencyMs").GetProperty("p50").GetDouble());

        var second = root.GetProperty("results")[1];
        Assert.Equal(JsonValueKind.Null, second.GetProperty("allocatedBytesPerCall").ValueKind);
        Assert.False(second.TryGetProperty("note", out _));
    }

    public async Task InitializeAsync() => _server = await MockHost.StartAsync(0, ResponsePath, CancellationToken.None);

    public async Task DisposeAsync() => await _server.DisposeAsync();
}
