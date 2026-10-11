using System.Net;
using Minos.SystemOne;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

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
                .WithBody(Fixture.Text("response-noul.json")));

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
                .WithBody(Fixture.Text("response-openrouter.json")));

        var baseAddress = new Uri(_fixture.BaseAddress, "api/");
        using var client = IntegrationClient.Create(baseAddress, provider: SystemOneProvider.OpenRouter);

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
                .WithBody(Fixture.Text("response-noul.json")));

        using var http = new HttpClient { BaseAddress = new Uri(_fixture.BaseAddress, "custom/") };
        using var client = new SystemOneClient(http, new SystemOneClientOptions { ApiKey = "integration-key", MaxRetries = 0 });

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task VllmSemanticRouter_WithoutAKey_PostsToV1SystemOne_WithNoAuthorizationHeader()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        using var client = new SystemOneClient(new SystemOneClientOptions
        {
            Provider = SystemOneProvider.VllmSemanticRouter,
            BaseAddress = _fixture.BaseAddress,
            Model = "router-model",
            MaxRetries = 0,
        });

        var result = await client.EvaluateAsync<NoulTriage>("Help! My payouts have been failing for 3 days.");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsUrgent.Value);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        var request = OnlyRequest();
        Assert.Equal("/v1/systemone", request.Path);
        Assert.False(request.Headers!.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Custom_WithAPath_PostsToThatPath()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/api/decide").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        using var client = new SystemOneClient(new SystemOneClientOptions
        {
            Provider = SystemOneProvider.Custom,
            BaseAddress = _fixture.BaseAddress,
            Path = "api/decide",
            Model = "local-model",
            MaxRetries = 0,
        });

        var result = await client.EvaluateAsync<NoulTriage>("Help! My payouts have been failing for 3 days.");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        Assert.Equal("/api/decide", OnlyRequest().Path);
    }

    [Fact]
    public async Task NoulOnlyServer_DeclaredNoulOnly_RefusesAChoiceSet_WithoutSendingIt()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(422)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-422-noul-only.json")));

        using var client = NoulOnlyServerClient(SystemOneQuestionKinds.Noul);

        var result = await client.EvaluateAsync<StructuredTriage>("Help! My payouts have been failing for 3 days.");

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal("Custom does not support Choice questions.", result.Error.Message);
        Assert.Equal([("MIN201", "department"), ("MIN201", "severity")], result.Error.Failures.Select(f => (f.Rule, f.QuestionKey)));
        Assert.Empty(_fixture.Server.LogEntries);
    }

    [Fact]
    public async Task NoulOnlyServer_WithTheDefaultCapabilities_ReturnsItsOwn422AsValidation()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(422)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-422-noul-only.json")));

        using var client = NoulOnlyServerClient(kinds: null);

        var result = await client.EvaluateAsync<StructuredTriage>("Help! My payouts have been failing for 3 days.");

        // The client does not guess that the kind was the problem: the server's answer comes back as it was sent.
        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        Assert.Equal(422, result.Error.StatusCode);
        Assert.Empty(result.Error.Failures);
        Assert.NotNull(result.Error.Detail);
        var problem = result.Error.Detail!.Value.GetProperty("detail")[0];
        Assert.Equal("Input should be 'noul'", problem.GetProperty("msg").GetString());
        Assert.Equal("department", problem.GetProperty("loc")[2].GetString());
        _ = OnlyRequest();
    }

    private SystemOneClient NoulOnlyServerClient(SystemOneQuestionKinds? kinds)
        => new(new SystemOneClientOptions
        {
            Provider = SystemOneProvider.Custom,
            BaseAddress = _fixture.BaseAddress,
            Model = "noul-small",
            MaxRetries = 0,
            Capabilities = new SystemOneCapabilities { QuestionKinds = kinds },
        });

    private IRequestMessage OnlyRequest()
    {
        IRequestMessage? only = null;
        // A log entry WireMock has already recorded always carries its RequestMessage.
        Assert.Collection(_fixture.Server.LogEntries, entry => only = entry.RequestMessage!);
        return only!;
    }
}
