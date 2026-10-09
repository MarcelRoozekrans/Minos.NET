using System.Text.Json;
using Minos.Benchmarks.Compare;
using Minos.Benchmarks.Compare.Adapters;
using Minos.Benchmarks.Mock;

namespace Minos.Benchmarks.Tests;

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
    public void Options_parse_the_cores_and_the_mock_cores()
    {
        var options = CompareOptions.Parse(["--base-url", "http://127.0.0.1:5005", "--out", "results", "--cores", "0-11", "--mock-cores", "0xFF000"], "default");

        Assert.Equal(0xFFFUL, options.Cores);
        Assert.Equal(0xFF000UL, options.MockCores);
    }

    [Fact]
    public void Options_parse_the_rotation()
    {
        var options = CompareOptions.Parse(["--base-url", "http://127.0.0.1:5005", "--out", "results", "--rotation", "3"], "default");

        Assert.Equal(3, options.Rotation);
    }

    [Fact]
    public void Options_default_the_machine_name_and_a_full_run()
    {
        var options = CompareOptions.Parse(["--base-url", "http://127.0.0.1:5005", "--out", "results"], "HOST-1");

        Assert.False(options.Smoke);
        Assert.Null(options.Cores);
        Assert.Null(options.MockCores);
        Assert.Null(options.Rotation);
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
    [InlineData("--base-url", "http://127.0.0.1:5005", "--out", "results", "--mock-cores", "x")]
    [InlineData("--base-url", "http://127.0.0.1:5005", "--out", "results", "--mock-cores")]
    [InlineData("--base-url", "http://127.0.0.1:5005", "--out", "results", "--rotation", "-1")]
    [InlineData("--base-url", "http://127.0.0.1:5005", "--out", "results", "--rotation", "x")]
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
        using var adapter = ClientAdapters.Create(ClientAdapters.Minos, BaseAddress);

        var phase = await ThroughputRunner.RunPhaseAsync(adapter, workers: 4, TimeSpan.FromMilliseconds(200));

        Assert.True(phase.Calls > 0);
        Assert.Equal(phase.Calls, await counter.CountAsync(CancellationToken.None));
        Assert.True(phase.PerSecond > 0);
    }

    [Fact]
    public async Task The_ceiling_is_the_best_of_every_worker_count()
    {
        // 32 workers is fastest here: 64 overloads the client, so a single 64-worker run would understate the ceiling.
        var rates = new Dictionary<int, long> { [16] = 9000, [32] = 12000, [64] = 10000 };
        var tried = new List<int>();

        var ceiling = await CeilingRunner.RunAsync(Harness.CeilingConcurrencies, workers =>
        {
            tried.Add(workers);
            return Task.FromResult(new ThroughputPhase(rates[workers], TimeSpan.FromSeconds(1)));
        });

        Assert.Equal([16, 32, 64], tried);
        Assert.Equal(new CeilingMeasurement(12000, 32), ceiling);
    }

    [Fact]
    public async Task The_ceiling_runs_each_worker_count_against_the_mock()
    {
        using var counter = new MockRequestCounter(BaseAddress);
        using var adapter = ClientAdapters.Create(ClientAdapters.Raw, BaseAddress);
        long calls = 0;

        var ceiling = await CeilingRunner.RunAsync([1, 4], async workers =>
        {
            var phase = await ThroughputRunner.RunPhaseAsync(adapter, workers, TimeSpan.FromMilliseconds(100));
            calls += phase.Calls;
            return phase;
        });

        Assert.True(ceiling.Workers is 1 or 4);
        Assert.True(ceiling.PerSecond > 0);
        Assert.Equal(calls, await counter.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_ceiling_needs_a_worker_count()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CeilingRunner.RunAsync([], _ => Task.FromResult(new ThroughputPhase(1, TimeSpan.FromSeconds(1)))));
    }

    [Fact]
    public void Latency_figures_are_the_mean_and_the_nearest_rank_percentiles()
    {
        // 1..100 ms, shuffled: the mean is 50.5, the 50th percentile is the 50th value and the 99th the 99th.
        var times = Enumerable.Range(1, 100).Select(i => (double)i).Reverse().ToList();

        var figures = LatencyRunner.Summarize(times);

        Assert.Equal(new LatencyFigures(50.5, 50, 99), figures);
    }

    [Fact]
    public void Latency_figures_of_one_call_are_that_call()
    {
        Assert.Equal(new LatencyFigures(2.5, 2.5, 2.5), LatencyRunner.Summarize([2.5]));
    }

    [Fact]
    public async Task The_latency_rounds_count_every_clients_calls_against_the_mock()
    {
        using var counter = new MockRequestCounter(BaseAddress);
        using var raw = ClientAdapters.Create(ClientAdapters.Raw, BaseAddress);
        using var minos = ClientAdapters.Create(ClientAdapters.Minos, BaseAddress);

        var runs = await LatencyRunner.RunRoundsAsync([raw, minos], warmupCalls: 3, rounds: 4, callsPerRound: 5, rotation: 1, counter.CountAsync, CancellationToken.None);

        // Each client: 3 warm-up calls, then 4 rounds of 5 timed calls.
        Assert.Equal([ClientAdapters.Raw, ClientAdapters.Minos], runs.Keys.Order(StringComparer.Ordinal));
        Assert.All(runs.Values, run =>
        {
            Assert.Equal(23, run.Calls);
            Assert.True(run.Latency.Mean > 0);
            Assert.True(run.Latency.P50 <= run.Latency.P99);
        });
        Assert.Equal(46, await counter.CountAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(3, 0, 0, new[] { 0, 1, 2 })]
    [InlineData(3, 1, 0, new[] { 1, 2, 0 })]
    [InlineData(3, 2, 0, new[] { 2, 0, 1 })]
    [InlineData(3, 3, 0, new[] { 0, 1, 2 })]
    [InlineData(3, 0, 2, new[] { 2, 0, 1 })]
    [InlineData(5, 7, 4, new[] { 1, 2, 3, 4, 0 })]
    public void The_order_rotates_by_round_from_the_rotation(int count, int round, int rotation, int[] expected)
    {
        Assert.Equal(expected, LatencyRunner.Order(count, round, rotation));
    }

    [Fact]
    public async Task The_latency_rounds_interleave_the_clients_and_pool_each_ones_times()
    {
        var calls = new List<string>();
        using var a = new RecordingAdapter("a", calls);
        using var b = new RecordingAdapter("b", calls);
        using var c = new RecordingAdapter("c", calls);

        var runs = await LatencyRunner.RunRoundsAsync(
            [a, b, c], warmupCalls: 1, rounds: 3, callsPerRound: 2, rotation: 1, _ => Task.FromResult((long)calls.Count), CancellationToken.None);

        // The warm-up runs in round 0's order; then round r starts at client (1 + r) mod 3, each client making 2 calls.
        Assert.Equal(
            ["b", "c", "a", "b", "b", "c", "c", "a", "a", "c", "c", "a", "a", "b", "b", "a", "a", "b", "b", "c", "c"],
            calls);
        Assert.All(runs.Values, run => Assert.Equal(7, run.Calls));
    }

    [Fact]
    public async Task A_client_whose_requests_differ_from_its_calls_fails_the_latency_rounds_by_name()
    {
        var calls = new List<string>();
        using var good = new RecordingAdapter("good", calls);
        using var retrying = new RecordingAdapter("retrying", calls);

        // Every call of "retrying" shows up as two requests at the mock.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LatencyRunner.RunRoundsAsync(
            [good, retrying], warmupCalls: 0, rounds: 2, callsPerRound: 3, rotation: 0,
            _ => Task.FromResult<long>(calls.Sum(name => string.Equals(name, "retrying", StringComparison.Ordinal) ? 2 : 1)),
            CancellationToken.None));

        Assert.StartsWith("retrying: the latency rounds made 6 calls but the mock served 12 requests.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_result_file_has_the_shared_format()
    {
        var file = new ResultFile(
            new MachineInfo("box", "Windows", "CPU", "2026-10-04T00:00:00Z", 30000.5, "0-11", "12-19"),
            [
                new ClientResult("jevsharp", "JevSharp", "0.2.0", ".NET", "10.0.0", new LatencyFigures(1.5, 1.25, 3), 9000, 16, 1024) { Note = "no auth header" },
                new ClientResult("jev-net", "Jev.Net", "0.4.0", ".NET", "10.0.0", new LatencyFigures(1, 1, 2), 8000, 16, null),
            ]);

        using var json = JsonDocument.Parse(file.ToJson());
        var root = json.RootElement;

        Assert.Equal(["machine", "results"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["name", "os", "cpu", "date", "mockCeilingPerSecond", "cores", "mockCores"], root.GetProperty("machine").EnumerateObject().Select(p => p.Name));
        Assert.Equal(30000.5, root.GetProperty("machine").GetProperty("mockCeilingPerSecond").GetDouble());
        Assert.Equal("0-11", root.GetProperty("machine").GetProperty("cores").GetString());
        Assert.Equal("12-19", root.GetProperty("machine").GetProperty("mockCores").GetString());
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

    [Fact]
    public void The_result_file_records_the_order_after_the_results()
    {
        var file = new ResultFile(
            new MachineInfo("box", "Linux", "CPU", "2026-10-04T00:00:00Z", 30000, null, null),
            [new ClientResult("jev-net", "Jev.Net", "0.4.0", ".NET", "10.0.0", new LatencyFigures(1, 1, 2), 8000, 16, 1024)])
        {
            Order = new MeasurementOrder(20, 100, 2, [ClientAdapters.JevSharp, ClientAdapters.TypeSafeSdk, ClientAdapters.JevNet, ClientAdapters.Minos, ClientAdapters.Raw]),
        };

        using var json = JsonDocument.Parse(file.ToJson());
        var order = json.RootElement.GetProperty("order");

        Assert.Equal(["machine", "results", "order"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["latencyRounds", "callsPerRound", "rotation", "throughput"], order.EnumerateObject().Select(p => p.Name));
        Assert.Equal(ClientAdapters.JevSharp, order.GetProperty("throughput")[0].GetString());
    }

    public async Task InitializeAsync() => _server = await MockHost.StartAsync(0, ResponsePath, CancellationToken.None);

    public async Task DisposeAsync() => await _server.DisposeAsync();

    // A client that answers at once and records each call, for checking the order without a server.
    private sealed class RecordingAdapter(string client, List<string> calls) : IClientAdapter
    {
        public string Client => client;

        public string Library => client;

        public string Version => "1.0.0";

        public string? Note => null;

        public ValueTask<CallOutcome> CallAsync(CancellationToken cancellationToken)
        {
            calls.Add(client);
            return ValueTask.FromResult(new CallOutcome(LooksUpBooking: true, Workload.TravelsSoonNoul));
        }

        public Task<WorkloadAnswers> AskAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
