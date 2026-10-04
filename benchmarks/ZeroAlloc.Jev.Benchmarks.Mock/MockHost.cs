using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace ZeroAlloc.Jev.Benchmarks.Mock;

/// <summary>Starts the WireMock server that serves the recorded Jev answer to every benchmark client.</summary>
public static class MockHost
{
    /// <summary>The most requests the request log keeps, so a long run never evicts the entries a harness counts.</summary>
    public const int MaxRequestLogCount = 100_000;

    /// <summary>Starts the server on <paramref name="port"/>; port 0 picks a free port.</summary>
    /// <param name="port">The port to listen on, on the loopback address.</param>
    /// <param name="responsePath">The file whose bytes <c>POST /v1/systemone</c> returns.</param>
    /// <returns>The running server; stop it to release the port.</returns>
    public static WireMockServer Start(int port, string responsePath)
    {
        var body = File.ReadAllBytes(responsePath);
        var server = WireMockServer.Start(new WireMockServerSettings
        {
            Urls = [$"http://127.0.0.1:{port}"],
            StartAdminInterface = true,
            MaxRequestLogCount = MaxRequestLogCount,
        });
        server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
        return server;
    }
}
