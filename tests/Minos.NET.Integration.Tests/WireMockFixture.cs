using System.Net;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Minos.Integration.Tests;

/// <summary>One WireMock server per test class; each test starts from <see cref="Reset"/>.</summary>
/// <remarks>
/// WireMock adds a request to <see cref="WireMockServer.LogEntries"/> after any response delay but before it writes the
/// response, so once a call that got a response has returned, the log holds every attempt it made. A request whose
/// response is still pending when its test ends can be logged after the next test's <see cref="Reset"/>, so a test that
/// leaves one pending, as <see cref="FailureTests"/> does, must not share a class with tests that read the log.
/// </remarks>
public sealed class WireMockFixture : IDisposable
{
    public WireMockFixture() => Server = WireMockServer.Start();

    public WireMockServer Server { get; }

    public Uri BaseAddress => new(Server.Urls[0] + "/");

    public void Reset() => Server.Reset();

    /// <summary>
    /// Makes the server hold every <c>POST /v1/systemone</c> response until the returned handle is disposed, so an attempt
    /// can only end through the client's per-attempt time-out, however late a busy machine schedules either side. A fixed
    /// WireMock delay is not enough: when the test host is starved of CPU, the time-out's own callback can run after the
    /// delay has elapsed and the response has already arrived.
    /// </summary>
    /// <returns>A handle that releases the held responses; dispose it only after the test has its result.</returns>
    public IDisposable HoldEveryResponse()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create().WithCallback(async _ =>
            {
                await release.Task.ConfigureAwait(false);
                return new ResponseMessage { StatusCode = (int)HttpStatusCode.OK };
            }));
        return new Release(release);
    }

    public void Dispose() => Server.Stop();

    /// <summary>Releases the responses <see cref="HoldEveryResponse"/> holds.</summary>
    private sealed class Release(TaskCompletionSource source) : IDisposable
    {
        public void Dispose() => source.TrySetResult();
    }
}
