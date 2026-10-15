using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.SystemOne;
using Minos.SystemOne.Serialization;
using static Minos.Tests.ClientTestKit;

namespace Minos.Tests;

/// <summary>OpenRouter provider preferences reach the request body as OpenRouter's <c>provider</c> object.</summary>
public sealed class ProviderPreferencesBodyTests : IDisposable
{
    private const string UrgencyResponse = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.9}},"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task TypedCall_WithBothFlags_SendsProviderObject()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, UrgencyResponse);
        using var client = OpenRouterClient(handler, zeroDataRetention: true, denyDataCollection: true);
        var request = new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket");

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsSuccess);
        var raw = OnlyRequest(handler).Body!;
        Assert.EndsWith(""","provider":{"zdr":true,"data_collection":"deny"}}""", raw, StringComparison.Ordinal);
        var sent = JsonNode.Parse(raw)!.AsObject();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"zdr":true,"data_collection":"deny"}"""), sent["provider"]));
        sent.Remove("provider");
        Assert.True(JsonNode.DeepEquals(ExpectedBody(request), sent), sent.ToJsonString());
    }

    [Fact]
    public async Task TypedCall_WithoutPreferences_BodyUnchanged()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, UrgencyResponse);
        using var client = Client(_httpClients, handler, provider: SystemOneProvider.OpenRouter);
        var request = new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket");

        await client.EvaluateAsync(request);

        Assert.Equal(ExpectedBody(request).ToJsonString(), JsonNode.Parse(OnlyRequest(handler).Body!)!.ToJsonString());
    }

    [Fact]
    public async Task CallWithImages_StillEndsWithProvider()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, UrgencyResponse);
        using var client = OpenRouterClient(handler, zeroDataRetention: true, denyDataCollection: false);
        var request = new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket")
        {
            Images = [DecisionImage.FromBytes(new byte[] { 1, 2, 3 }, "image/png")],
        };

        await client.EvaluateAsync(request);

        var sent = JsonNode.Parse(OnlyRequest(handler).Body!)!;
        Assert.IsType<JsonArray>(sent["state"]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"zdr":true}"""), sent["provider"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("minos-x")]
    public async Task RawCall_WithPreferences_SendsProviderObject(string? model)
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, UrgencyResponse);
        using var client = OpenRouterClient(handler, zeroDataRetention: true, denyDataCollection: false);
        var request = RawRequest(model);

        await client.EvaluateAsync(request, CancellationToken.None);

        var sent = JsonNode.Parse(OnlyRequest(handler).Body!)!.AsObject();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"zdr":true}"""), sent["provider"]));
        Assert.Equal(model ?? TestModel, sent["model"]!.GetValue<string>());
        Assert.Equal("a ticket", sent["state"]!.GetValue<string>());
        Assert.NotNull(sent["questions"]!["is_urgent"]);
        Assert.Equal(model, request.Model);
    }

    [Fact]
    public async Task RawCall_WithoutPreferences_BodyUnchanged()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, UrgencyResponse);
        using var client = Client(_httpClients, handler, provider: SystemOneProvider.OpenRouter);

        await client.EvaluateAsync(RawRequest("minos-x"), CancellationToken.None);

        Assert.False(JsonNode.Parse(OnlyRequest(handler).Body!)!.AsObject().ContainsKey("provider"));
    }

    [Fact]
    public void SystemOneRequest_SerializedByCaller_HasNoProvider()
    {
        var json = JsonSerializer.Serialize(RawRequest("minos-x"), DecisionJsonContext.Default.SystemOneRequest);

        Assert.False(JsonNode.Parse(json)!.AsObject().ContainsKey("provider"));
    }

    [Fact]
    public async Task OpenRouterRefusal_IsNotRetried()
    {
        const string Refusal = """{"error":{"message":"No endpoints found matching your data policy","code":404}}""";
        var handler = StubHandler.Json(HttpStatusCode.NotFound, Refusal);
        using var client = Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.OpenRouter,
            configure: options =>
            {
                options.MaxRetries = 2;
                options.ProviderPreferences = new() { ZeroDataRetention = true };
            });

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket"));

        Assert.True(result.IsFailure);
        Assert.Equal(404, result.Error.StatusCode);
        Assert.Equal("The API returned HTTP 404.", result.Error.Message);
        Assert.Contains("No endpoints found matching your data policy", result.Error.Detail?.ToString(), StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    private static SystemOneRequest RawRequest(string? model) => new()
    {
        Model = model,
        State = "a ticket",
        Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
        {
            ["is_urgent"] = new NoulQuestion { Instructions = "Is this urgent?" },
        },
    };

    private SystemOneDecisionClient OpenRouterClient(StubHandler handler, bool zeroDataRetention, bool denyDataCollection)
        => Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.OpenRouter,
            configure: options =>
            {
                // Images are opt-in for OpenRouter, as in the AOT smoke app's image round trip.
                options.Capabilities = new DecisionCapabilities { SupportsImages = true };
                options.ProviderPreferences = new()
                {
                    ZeroDataRetention = zeroDataRetention,
                    DenyDataCollection = denyDataCollection,
                };
            });
}
