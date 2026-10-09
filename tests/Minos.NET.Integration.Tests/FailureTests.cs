using System.Net;
using System.Net.Sockets;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Minos.Integration.Tests;

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
        // The server holds every response until the test has its result; see HoldEveryResponse.
        using var held = _fixture.HoldEveryResponse();

        // Attempts are counted on the client: WireMock records a request only once it has read it, and an
        // attempt the client abandons before WireMock gets to it is never recorded at all.
        using var attempts = new AttemptRecorder(_fixture.BaseAddress, timeout: TimeSpan.FromMilliseconds(300));
        using var client = IntegrationClient.Create(attempts, maxRetries: 1);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Timeout, result.Error.Kind);
        Assert.Equal(2, attempts.Count);
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
        // The server signals when it has the request and then holds the response until the test ends. Cancelling
        // only after that signal puts the cancellation mid-request by construction, rather than by a 200 ms guess
        // that a busy machine can overrun, and while the response is held the call can only end through the
        // cancellation, so no wall-clock bound has to separate "cancelled" from "answered".
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create().WithCallback(async _ =>
            {
                received.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new ResponseMessage { StatusCode = (int)HttpStatusCode.OK };
            }));

        try
        {
            using var client = IntegrationClient.Create(_fixture.BaseAddress);
            using var cancellation = new CancellationTokenSource();

            var call = client.EvaluateAsync(Fixtures.NoulRequest(), cancellation.Token).AsTask();
            await received.Task.WaitAsync(HangGuard);
            await cancellation.CancelAsync();

            // HangGuard only stops a regression that ignores the token from hanging the run: it then surfaces as a
            // TimeoutException, which fails the assertion below.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(HangGuard));
        }
        finally
        {
            release.SetResult();
        }
    }

    [Fact]
    public async Task DisposingTheClient_MidRequest_IsDisposed_AndNotRetried()
    {
        // As in CallerCancellation_MidRequest_Throws, the server signals when it has the request and holds the response,
        // so the disposal lands mid-request by construction. The client owns its HttpClient and a real
        // SocketsHttpHandler, so disposing it tears the request down as it would in production.
        // The test counts requests on the server, so it gets a server of its own: an attempt another test in this class
        // abandoned, such as SlowResponse_TimesOutPerAttempt's, can reach the shared server late and would match this
        // test's mapping, counting as a second request.
        using var server = WireMockServer.Start();
        var received = 0;
        var firstReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create().WithCallback(async _ =>
            {
                Interlocked.Increment(ref received);
                firstReceived.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new ResponseMessage { StatusCode = (int)HttpStatusCode.OK };
            }));

        try
        {
            var client = IntegrationClient.Create(new Uri(server.Urls[0] + "/"), maxRetries: 2);

            var call = client.EvaluateAsync(Fixtures.NoulRequest()).AsTask();
            await firstReceived.Task.WaitAsync(HangGuard);
            client.Dispose();
            var result = await call.WaitAsync(HangGuard);

            Assert.True(result.IsFailure);
            Assert.Equal(JevErrorKind.Disposed, result.Error.Kind);
            Assert.Equal(1, Volatile.Read(ref received));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(Fixtures.NoulRequest()));
        }
        finally
        {
            release.SetResult();
        }
    }

    // Far longer than any scheduling delay a loaded machine adds; reached only when the code under test is broken.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);
}
