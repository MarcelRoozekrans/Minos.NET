using System.Net;
using System.Net.Sockets;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ZeroAlloc.Jev.Integration.Tests;

public sealed class FailureTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public FailureTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task SlowResponse_TimesOutPerAttempt()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixtures.Text("response-noul.json"))
                .WithDelay(TimeSpan.FromSeconds(2)));

        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 1, timeout: TimeSpan.FromMilliseconds(300));

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Timeout, result.Error.Kind);

        // WireMock only logs a request once its (delayed) response has finished, so the log can still be
        // catching up to the two client attempts once EvaluateAsync has already returned.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_fixture.Server.LogEntries.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(2, _fixture.Server.LogEntries.Count);
    }

    [Fact]
    public async Task RefusedConnection_IsNetwork()
    {
        // A socket that is bound but never Listen()s holds the port for the whole test: the kernel refuses the
        // TCP handshake immediately (no listener is attached to it), and nobody else can bind the same port while
        // this socket holds it. That is deterministic on a busy CI runner; starting and then stopping a real
        // WireMock server is not, because another process can grab the freed port before the client connects.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        var baseAddress = new Uri("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");

        using var client = IntegrationClient.Create(baseAddress, maxRetries: 0);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Network, result.Error.Kind);
    }

    [Fact]
    public async Task CallerCancellation_MidRequest_Throws()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixtures.Text("response-noul.json"))
                .WithDelay(TimeSpan.FromSeconds(5)));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        var task = Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.EvaluateAsync(Fixtures.NoulRequest(), cancellation.Token));

        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(4)));
        Assert.Same(task, completed);
        await task;
    }
}
