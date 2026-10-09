using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

/// <summary>A real-socket evaluation's log events, through the owned-client logging constructor.</summary>
public sealed class LoggingTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public LoggingTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task RetriedEvaluation_LogsOneAttemptRetrying_ThenEvaluationSucceeded()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("logging-retry")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("logging-retry")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        using var logs = new LogCapture();
        using var client = new JevClient(
            new JevClientOptions
            {
                ApiKey = "integration-key",
                BaseAddress = _fixture.BaseAddress,
                MaxRetries = 2,
                InitialBackoff = TimeSpan.FromMilliseconds(10),
                Jitter = false,
            },
            logs.Factory);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _fixture.Server.LogEntries.Count);
        Assert.Equal([1003, 1001], logs.EventIds);

        var retrying = logs.Only(1003);
        Assert.Equal("1", LogAssert.Field(retrying, "Attempt"));
        Assert.Equal("Overloaded", LogAssert.Field(retrying, "ErrorKind"));
        Assert.Equal("503", LogAssert.Field(retrying, "StatusCode"));

        var succeeded = logs.Only(1001);
        Assert.Equal("Minos.JevClient", succeeded.Category);
        Assert.Equal("evaluate", LogAssert.Field(succeeded, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(succeeded, "Model"));
        Assert.Equal("TypeSafe", LogAssert.Field(succeeded, "Provider"));
        Assert.Equal("1", LogAssert.Field(succeeded, "QuestionCount"));
    }
}
