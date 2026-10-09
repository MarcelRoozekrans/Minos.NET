using System.Net;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

public sealed class WireFormatTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public WireFormatTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task Evaluate_SendsHeadersAndBody()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var entry = Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
        // A log entry WireMock has already recorded always carries its RequestMessage.
        var sent = entry.RequestMessage!;

        Assert.Equal("/v1/systemone", sent.Path);
        Assert.Equal("Bearer integration-key", sent.Headers!["Authorization"][0]);
        Assert.StartsWith("Minos.NET/", sent.Headers["User-Agent"][0]);
        Assert.StartsWith("application/json", sent.Headers["Content-Type"][0]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Fixture.Text("request-noul.json")), JsonNode.Parse(sent.Body!)));
    }

    [Fact]
    public async Task ListModels_SendsGetWithAuthorization()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/models").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("models.json")));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Models);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var entry = Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
        // A log entry WireMock has already recorded always carries its RequestMessage.
        var sent = entry.RequestMessage!;
        Assert.Equal("GET", sent.Method);
        Assert.Equal("Bearer integration-key", sent.Headers!["Authorization"][0]);
        Assert.StartsWith("Minos.NET/", sent.Headers["User-Agent"][0]);
    }
}
