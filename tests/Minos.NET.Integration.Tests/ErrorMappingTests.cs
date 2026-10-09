using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

public sealed class ErrorMappingTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public ErrorMappingTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task Status401_IsUnauthorized_WithBody()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Unauthorized)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":"bad key"}"""));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unauthorized, result.Error.Kind);
        Assert.Equal(401, result.Error.StatusCode);
        Assert.NotNull(result.Error.Detail);
        Assert.Equal("bad key", result.Error.Detail!.Value.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Status422_IsValidation_WithBody()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(422)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":"bad key"}"""));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        Assert.Equal(422, result.Error.StatusCode);
        Assert.NotNull(result.Error.Detail);
        Assert.Equal("bad key", result.Error.Detail!.Value.GetProperty("error").GetString());
    }

    // DecisionError exposes no raw response body and no truncation flag (only Kind, Message, StatusCode, RetryAfter,
    // Detail and Exception - see src/Minos.NET/DecisionError.cs). The only observable signal of the 16 KiB
    // (IDecisionApi's MaxErrorBodyBytes) cutoff is Detail: DecisionErrorMapper.Detail returns null whenever the body was
    // truncated, before it even tries to parse. So this sends a body that is otherwise-valid JSON once complete -
    // if it were not truncated, Detail would parse successfully - and asserts Detail is null, which is the kept
    // body being cut off rather than the body simply failing to parse.
    [Fact]
    public async Task LargeErrorBody_IsTruncated()
    {
        var body = "{\"error\":\"" + new string('x', 20_000) + "\"}";

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.InternalServerError)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 0);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Server, result.Error.Kind);
        Assert.Equal(500, result.Error.StatusCode);
        Assert.Null(result.Error.Detail);
    }

    [Fact]
    public async Task MalformedSuccess_IsInvalidResponse()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody("not json"));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
    }
}
