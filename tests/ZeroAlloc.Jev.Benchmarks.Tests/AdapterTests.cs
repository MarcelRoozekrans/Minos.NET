using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;
using ZeroAlloc.Jev.Benchmarks.Compare;
using ZeroAlloc.Jev.Benchmarks.Compare.Adapters;
using ZeroAlloc.Jev.Benchmarks.Mock;

namespace ZeroAlloc.Jev.Benchmarks.Tests;

/// <summary>Each comparison client against the in-process mock and a logging twin: it reaches the mock, sends the workload and parses the recorded answer.</summary>
public sealed class AdapterTests : IAsyncLifetime, IDisposable
{
    private static readonly string ResponsePath = Path.Combine(AppContext.BaseDirectory, "response.json");

    private MockServer _server = null!;

    // The mock keeps no request log, so the tests that inspect what a client sent use this server, which logs every
    // request and answers with the same recorded bytes.
    private readonly WireMockServer _recorder = StartRecorder();

    private MockRequestCounter _counter = null!;

    public static TheoryData<string> Clients { get; } = new(ClientAdapters.All);

    private Uri BaseAddress => _server.BaseAddress;

    private Uri RecorderAddress => new(_recorder.Urls[0]);

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task One_call_reads_the_recorded_answers(string client)
    {
        using var adapter = ClientAdapters.Create(client, BaseAddress);

        var answers = await adapter.AskAsync(CancellationToken.None);

        answers.EnsureEquals(client, Workload.Expected);
        Assert.Equal(1, await _counter.CountAsync(CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task The_measured_call_reads_the_expected_outcome(string client)
    {
        using var adapter = ClientAdapters.Create(client, BaseAddress);

        var outcome = await adapter.CallAsync(CancellationToken.None);

        Assert.True(outcome.IsExpected);
        Assert.Equal(1, await _counter.CountAsync(CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task Every_client_sends_the_workload_to_systemone(string client)
    {
        using var adapter = ClientAdapters.Create(client, RecorderAddress);

        _ = await adapter.CallAsync(CancellationToken.None);

        var request = OnlyRequest(_recorder);
        Assert.Equal("/v1/systemone", request.Path);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal(Workload.Model, root.GetProperty("model").GetString());
        Assert.Equal(Workload.State, root.GetProperty("state").GetString());

        var questions = root.GetProperty("questions");
        Assert.Equal([Workload.IntentKey, Workload.TravelsSoonKey], questions.EnumerateObject().Select(p => p.Name));

        var intent = questions.GetProperty(Workload.IntentKey);
        Assert.Equal("choice", intent.GetProperty("type").GetString());
        Assert.Equal(Workload.IntentInstructions, intent.GetProperty("instructions").GetString());
        Assert.Equal(
            Workload.IntentOptions.Select(o => (o.Key, o.Value)),
            intent.GetProperty("criteria").EnumerateObject().Select(p => (p.Name, p.Value.GetString()!)));

        var travelsSoon = questions.GetProperty(Workload.TravelsSoonKey);
        Assert.Equal("noul", travelsSoon.GetProperty("type").GetString());
        Assert.Equal(Workload.TravelsSoonInstructions, travelsSoon.GetProperty("instructions").GetString());
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task Every_client_runs_on_the_benchmark_transport(string client)
    {
        using var adapter = ClientAdapters.Create(client, RecorderAddress);

        _ = await adapter.CallAsync(CancellationToken.None);

        // The adapter's library sends through a client BenchmarkTransport made.
        Assert.True(BenchmarkTransport.IsFromHere(((IUsesBenchmarkTransport)adapter).Http));

        // BenchmarkTransport's handler has automatic decompression off, so no client asks for a compressed answer.
        // Jev.Net's own default handler turns decompression on and would send Accept-Encoding.
        var request = OnlyRequest(_recorder);
        Assert.False(request.Headers!.ContainsKey("Accept-Encoding"));
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task Every_client_but_JevSharp_sends_the_dummy_key(string client)
    {
        using var adapter = ClientAdapters.Create(client, RecorderAddress);

        _ = await adapter.CallAsync(CancellationToken.None);

        var headers = OnlyRequest(_recorder).Headers!;
        if (string.Equals(client, ClientAdapters.JevSharp, StringComparison.Ordinal))
        {
            // JevSharp reaches the mock through a custom endpoint, which takes static headers only, and sends none.
            Assert.False(headers.ContainsKey("Authorization"));
            Assert.NotNull(adapter.Note);
        }
        else
        {
            Assert.Equal(["Bearer " + Workload.DummyApiKey], headers["Authorization"]);
            Assert.Null(adapter.Note);
        }
    }

    [Fact]
    public async Task The_raw_baseline_sends_the_body_ZeroAlloc_Jev_sends()
    {
        using var jev = ClientAdapters.Create(ClientAdapters.Jev, RecorderAddress);
        using var raw = ClientAdapters.Create(ClientAdapters.Raw, RecorderAddress);

        _ = await jev.CallAsync(CancellationToken.None);
        _ = await raw.CallAsync(CancellationToken.None);

        byte[]? jevBody = null;
        byte[]? rawBody = null;
        Assert.Collection(
            _recorder.LogEntries,
            e => jevBody = e.RequestMessage?.BodyAsBytes,
            e => rawBody = e.RequestMessage?.BodyAsBytes);
        Assert.NotNull(jevBody);
        Assert.NotEmpty(jevBody);
        Assert.Equal(jevBody, rawBody);
    }

    [Fact]
    public void The_benchmark_transport_has_the_ruled_handler_settings()
    {
        using var handler = BenchmarkTransport.CreateHandler();

        Assert.Equal(TimeSpan.FromMinutes(2), handler.PooledConnectionLifetime);
        Assert.Equal(System.Net.DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(int.MaxValue, handler.MaxConnectionsPerServer);
    }

    [Fact]
    public void A_client_made_elsewhere_is_not_from_the_benchmark_transport()
    {
        using var other = new HttpClient();
        using var ours = BenchmarkTransport.CreateHttpClient();

        Assert.False(BenchmarkTransport.IsFromHere(other));
        Assert.True(BenchmarkTransport.IsFromHere(ours));
    }

    [Fact]
    public async Task The_raw_baseline_sends_a_content_length_not_a_chunked_body()
    {
        using var raw = ClientAdapters.Create(ClientAdapters.Raw, RecorderAddress);

        _ = await raw.CallAsync(CancellationToken.None);

        var request = OnlyRequest(_recorder);
        var headers = request.Headers!;
        Assert.Equal([request.BodyAsBytes!.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)], headers["Content-Length"]);
        Assert.False(headers.ContainsKey("Transfer-Encoding"));
        Assert.Equal(["application/json"], headers["Content-Type"]);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task A_failed_call_is_attempted_once(string client)
    {
        using var failing = WireMockServer.Start(new WireMockServerSettings { Urls = ["http://127.0.0.1:0"] });
        failing.Given(Request.Create().WithPath("/v1/systemone").UsingPost()).RespondWith(Response.Create().WithStatusCode(503));
        using var adapter = ClientAdapters.Create(client, new Uri(failing.Urls[0]));

        await Assert.ThrowsAnyAsync<Exception>(async () => await adapter.CallAsync(CancellationToken.None));

        _ = OnlyRequest(failing);
    }

    // The one request the server logged; fails unless there is exactly one.
    private static WireMock.IRequestMessage OnlyRequest(WireMockServer server)
    {
        WireMock.IRequestMessage? only = null;
        Assert.Collection(server.LogEntries, e => only = e.RequestMessage);
        Assert.NotNull(only);
        return only;
    }

    public async Task InitializeAsync()
    {
        _server = await MockHost.StartAsync(0, ResponsePath, CancellationToken.None);
        _counter = new MockRequestCounter(BaseAddress);
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    public void Dispose()
    {
        _counter.Dispose();
        _recorder.Stop();
        _recorder.Dispose();
    }

    private static WireMockServer StartRecorder()
    {
        var recorder = WireMockServer.Start(new WireMockServerSettings { Urls = ["http://127.0.0.1:0"] });
        recorder
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(File.ReadAllBytes(ResponsePath)));
        return recorder;
    }
}
