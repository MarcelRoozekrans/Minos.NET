using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZeroAlloc.Jev.Serialization;
using static ZeroAlloc.Jev.Tests.ClientTestKit;

namespace ZeroAlloc.Jev.Tests;

/// <summary>A built set evaluates the same through the default interface methods and through JevClient's pooled path.</summary>
public sealed class JevClientQuestionSetTests : IDisposable
{
    private const string ResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95},"department":{"type":"choice","choice":"technical","probabilities":{"billing":0.1,"technical":0.8,"sales":0.1},"confidence":0.8},"product":{"type":"choice","choice":"pro-plan","probabilities":{"pro-plan":0.6,"team-plan":0.4},"confidence":0.6},"effort":{"type":"score","score":1.2,"legend":{"0":"Minutes","1":"Hours","2":"Days"},"probabilities":{"0":0.1,"1":0.6,"2":0.3},"confidence":0.7}},"usage":{"input_tokens":10,"output_tokens":5}}""";

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public Task TextState_BothPathsAgree() => AssertBothPathsAgree("Help! My payouts have been failing for 3 days.");

    [Fact]
    public Task JsonState_BothPathsAgree()
        => AssertBothPathsAgree(JevContent.FromUtf8Json("""{"messages":[{"role":"user","content":"Help!"}]}"""u8));

    [Fact]
    public void InvalidArguments_ThrowSynchronously_WithoutARequest()
    {
        var set = Set(out _, out _, out _, out _);
        var handler = StubHandler.Json(HttpStatusCode.OK, ResponseJson);
        var pool = new CountingPool();
        using var client = Client(handler, pool);
        IJevClient fake = new CapturingClient(ResponseJson);

        Assert.Equal("questionSet", ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync((JevQuestionSet)null!, "x").AsTask()).ParamName);
        Assert.Equal("state", ThrowsSynchronously<ArgumentException>(() => client.EvaluateAsync(set, default(JevContent)).AsTask()).ParamName);
        Assert.Equal("questionSet", ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync((JevQuestionSet)null!, "x").AsTask()).ParamName);
        Assert.Equal("state", ThrowsSynchronously<ArgumentException>(() => fake.EvaluateAsync(set, default(JevContent)).AsTask()).ParamName);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task DisposedClient_Throws_AfterCheckingArguments()
    {
        var set = Set(out _, out _, out _, out _);
        var client = Client(StubHandler.Json(HttpStatusCode.OK, ResponseJson));
        client.Dispose();

        ThrowsSynchronously<ArgumentNullException>(() => client.EvaluateAsync((JevQuestionSet)null!, "x").AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(set, "x"));
    }

    [Fact]
    public async Task MissingAnswer_IsInvalidResponse_AndReturnsTheBuffers()
    {
        var set = Set(out _, out _, out _, out _);
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")), pool);

        var result = await client.EvaluateAsync(set, "x");

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.IsType<JsonException>(result.Error.Exception);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task CallerCancellation_Throws_AndReturnsTheBuffers()
    {
        var set = Set(out _, out _, out _, out _);
        var pool = new CountingPool();
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, ResponseJson), pool);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(set, "x", cancellation.Token));

        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
    }

    private async Task AssertBothPathsAgree(JevContent state)
    {
        var set = Set(out var urgent, out var department, out var product, out var effort);
        var fake = new CapturingClient(ResponseJson);

        var viaDefault = await ((IJevClient)fake).EvaluateAsync(set, state);
        var expected = JsonNode.Parse(JsonSerializer.Serialize(fake.OnlyRequest(), JevJsonContext.Default.SystemOneRequest))!;
        Assert.Equal(JevDefaults.Model, expected["model"]!.GetValue<string>());
        expected["model"] = TestModel;

        var handler = StubHandler.Json(HttpStatusCode.OK, ResponseJson);
        var pool = new CountingPool();
        using var client = Client(handler, pool);
        var viaClient = await client.EvaluateAsync(set, state, CancellationToken.None);

        var sent = JsonNode.Parse(OnlyRequest(handler).Body!);
        Assert.True(JsonNode.DeepEquals(expected, sent), sent?.ToJsonString());
        Assert.True(viaDefault.IsSuccess);
        Assert.True(viaClient.IsSuccess);
        Assert.Equal(viaDefault.Value.Get(urgent).Probability, viaClient.Value.Get(urgent).Probability);
        Assert.Equal(viaDefault.Value.Get(department), viaClient.Value.Get(department));
        Assert.Equal(viaDefault.Value.Get(product), viaClient.Value.Get(product));
        Assert.Equal(viaDefault.Value.Get(effort), viaClient.Value.Get(effort));
        Assert.True(pool.Rented >= 2, $"rented {pool.Rented}");
        Assert.Equal(0, pool.Outstanding);
    }

    private static JevQuestionSet Set(
        out NoulHandle urgent, out ChoiceHandle<Department> department, out KeyedChoiceHandle product, out KeyedScoreHandle effort)
    {
        var built = JevQuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out urgent)
            .Choice<Department>("department", "Which team should handle this?", out department, o => o
                .Describe(Department.Billing, "Payments, invoicing, refunds"))
            .Choice("product", "Which product?", out product, o => o.Option("pro-plan", "The Pro subscription").Option("team-plan"))
            .Score("effort", "How much effort?", out effort, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);
        return built.Value;
    }

    private JevClient Client(StubHandler handler, CountingPool? pool = null) => ClientTestKit.Client(_httpClients, handler, pool);
}
