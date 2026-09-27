using WireMock.Server;

namespace ZeroAlloc.Jev.Integration.Tests;

/// <summary>One WireMock server per test class; each test starts from <see cref="Reset"/>.</summary>
public sealed class WireMockFixture : IDisposable
{
    public WireMockFixture() => Server = WireMockServer.Start();

    public WireMockServer Server { get; }

    public Uri BaseAddress => new(Server.Urls[0] + "/");

    public void Reset() => Server.Reset();

    public void Dispose() => Server.Stop();
}
