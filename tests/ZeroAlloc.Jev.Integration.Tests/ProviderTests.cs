using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ZeroAlloc.Jev.Integration.Tests;

public sealed class ProviderTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public ProviderTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task PathPrefixedBaseAddress_ReachesPrefixedEndpoint()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/api/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixtures.Text("response-noul.json")));

        var baseAddress = new Uri(_fixture.BaseAddress, "api/");
        using var client = IntegrationClient.Create(baseAddress);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var entry = Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
        // A log entry WireMock has already recorded always carries its RequestMessage.
        Assert.Equal("/api/v1/systemone", entry.RequestMessage!.Path);
    }

    [Fact]
    public async Task OpenRouterResponse_ParsesIdProviderAndCost()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/api/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixtures.Text("response-openrouter.json")));

        var baseAddress = new Uri(_fixture.BaseAddress, "api/");
        using var client = IntegrationClient.Create(baseAddress, provider: JevProvider.OpenRouter);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("gen-1727400000-abc123", result.Value.Id);
        Assert.Equal("TypeSafe", result.Value.Provider);
        Assert.Equal(0.000296, result.Value.Usage.Cost);
    }

    [Fact]
    public async Task BorrowedHttpClient_UsesItsOwnBaseAddress()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/custom/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixtures.Text("response-noul.json")));

        using var http = new HttpClient { BaseAddress = new Uri(_fixture.BaseAddress, "custom/") };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "integration-key", MaxRetries = 0 });

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
    }
}
