using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

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
        var server = WireMockServer.Start();
        var baseAddress = new Uri(server.Urls[0] + "/");
        server.Stop();

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
