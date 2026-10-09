using System.Diagnostics;
using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

/// <summary>A retried evaluation over real sockets: one Jev span parents each attempt's ZeroAlloc.Rest span.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class TelemetryTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public TelemetryTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task RetriedEvaluation_IsOneDecisionSpan_OverTwoRestSpans()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("telemetry-retry")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("telemetry-retry")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        using var capture = new TelemetryCapture(rest: true);
        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _fixture.Server.LogEntries.Count);

        var clientSpan = capture.Span();
        Assert.Equal(ActivityKind.Client, clientSpan.Kind);
        Assert.Equal(ActivityStatusCode.Unset, clientSpan.Status);
        Assert.Equal(_fixture.BaseAddress.Host, clientSpan.GetTagItem("server.address"));
        Assert.Equal(_fixture.BaseAddress.Port, clientSpan.GetTagItem("server.port"));

        var rest = capture.Spans("ZeroAlloc.Rest");
        Assert.Equal(2, rest.Length);
        Assert.All(rest, attempt =>
        {
            Assert.Equal(clientSpan.TraceId, attempt.TraceId);
            Assert.Equal(clientSpan.SpanId, attempt.ParentSpanId);
        });
    }
}
