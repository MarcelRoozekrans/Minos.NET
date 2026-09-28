using WireMock.Server;

namespace ZeroAlloc.Jev.Integration.Tests;

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

    public void Dispose() => Server.Stop();
}
