using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.SystemOne;
using Minos.SystemOne.Serialization;

namespace Minos.Tests;

/// <summary>What the raw <c>EvaluateAsync(SystemOneRequest)</c> does with the request's model and with a malformed request.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class SystemOneRawRequestTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task NullModel_SendsTheClientsModel()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler);

        var result = await client.EvaluateAsync(Request(model: null));

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientTestKit.TestModel, SentModel(handler));
    }

    [Fact]
    public async Task NullModel_OnACustomProvider_SendsTheConfiguredModel()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.Custom,
            configure: options =>
            {
                options.BaseAddress = new Uri("https://example.test/api/");
                options.Model = "my-model";
            });

        var result = await client.EvaluateAsync(Request(model: null));

        Assert.True(result.IsSuccess);
        Assert.Equal("my-model", SentModel(handler));
    }

    [Fact]
    public async Task NullModel_WithoutAnOptionsModel_SendsThePresetsDefault()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, configure: options => options.Model = null);

        await client.EvaluateAsync(Request(model: null));

        Assert.Equal(SystemOneDefaults.Model, SentModel(handler));
    }

    [Fact]
    public async Task NamedModel_WinsOverTheClientsModel()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler);

        await client.EvaluateAsync(Request(model: "jev-1.13.0"));

        Assert.Equal("jev-1.13.0", SentModel(handler));
    }

    [Fact]
    public async Task NullModel_TagsTheSpanWithTheResolvedModel()
    {
        using var capture = new TelemetryCapture();
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")));

        await client.EvaluateAsync(Request(model: null));

        Assert.Equal(ClientTestKit.TestModel, capture.StartTags().Tag("gen_ai.request.model"));
        Assert.Equal("evaluate " + ClientTestKit.TestModel, capture.Span().DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankModel_ThrowsArgumentException_WithoutSending(string model)
        => AssertRejected(Request(model));

    [Fact]
    public void DefaultState_ThrowsArgumentException_WithoutSending()
        => AssertRejected(new SystemOneRequest { State = default, Questions = Questions() });

    [Fact]
    public void NullQuestions_ThrowsArgumentException_WithoutSending()
        => AssertRejected(new SystemOneRequest { State = DecisionContent.FromString("s"), Questions = null! });

    [Fact]
    public void NullQuestion_ThrowsArgumentException_WithoutSending()
        => AssertRejected(new SystemOneRequest
        {
            State = DecisionContent.FromString("s"),
            Questions = new Dictionary<string, Question> { ["q"] = null! },
        });

    [Fact]
    public void DefaultInstructions_ThrowsArgumentException_WithoutSending()
        => AssertRejected(new SystemOneRequest
        {
            State = DecisionContent.FromString("s"),
            Questions = new Dictionary<string, Question> { ["q"] = new NoulQuestion { Instructions = default } },
        });

    [Fact]
    public void ADefaultInstructionsOnAChoiceQuestion_IsRejectedToo()
        => AssertRejected(new SystemOneRequest
        {
            State = DecisionContent.FromString("s"),
            Questions = new Dictionary<string, Question>
            {
                ["q"] = new ChoiceQuestion { Instructions = default, Criteria = new Dictionary<string, DecisionContent?> { ["a"] = null, ["b"] = null } },
            },
        });

    private void AssertRejected(SystemOneRequest request)
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler);

        var exception = ClientTestKit.ThrowsSynchronously<ArgumentException>(() => client.EvaluateAsync(request).AsTask());

        Assert.Equal("request", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    private static string? SentModel(StubHandler handler)
        => JsonNode.Parse(Assert.Single(handler.Requests).Body!)!["model"]?.GetValue<string>();

    private static SystemOneRequest Request(string? model)
        => new() { State = DecisionContent.FromString("s"), Model = model, Questions = Questions() };

    private static Dictionary<string, Question> Questions()
        => new() { ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" } };
}
