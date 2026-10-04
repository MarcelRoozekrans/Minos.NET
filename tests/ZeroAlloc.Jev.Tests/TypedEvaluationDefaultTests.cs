using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Tests;

public sealed record TicketContext(string Subject, string Body);

[JsonSerializable(typeof(TicketContext))]
internal sealed partial class TicketContextJsonContext : JsonSerializerContext;

[JevQuestions(State = typeof(TicketContext))]
public partial record TicketUrgency
{
    [Noul("Does the ticket convey urgency?")]
    public partial Noul IsUrgent { get; }
}

/// <summary>Covers the typed <c>EvaluateAsync</c> default interface methods on <see cref="IJevClient"/>.</summary>
public sealed class TypedEvaluationDefaultTests
{
    [Fact]
    public async Task String_ReturnsTypedAnswers()
    {
        IJevClient client = FakeClient.Returning("response-noul.json");

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        Assert.True(result.Value.IsUrgent.Value);
    }

    [Fact]
    public async Task String_SendsTheGeneratedQuestionsTheStateAndTheDefaultModel()
    {
        var fake = FakeClient.Returning("response-noul.json");

        await ((IJevClient)fake).EvaluateAsync<UrgencyCheck>("text");

        var request = fake.OnlyRequest();
        Assert.True(request.State.TryGetString(out var state));
        Assert.Equal("text", state);
        Assert.Equal(JevDefaults.Model, request.Model);
        AssertQuestionsEqual(UrgencyCheck.QuestionsUtf8, request);
    }

    [Fact]
    public async Task CancellationToken_IsForwarded()
    {
        var fake = FakeClient.Returning("response-noul.json");
        using var cts = new CancellationTokenSource();

        await ((IJevClient)fake).EvaluateAsync<UrgencyCheck>("text", cts.Token);

        Assert.Equal(cts.Token, fake.LastToken);
    }

    [Fact]
    public async Task JsonElement_And_Utf8_SendTheSameState()
    {
        const string json = """{"messages":[{"role":"user","content":"Help!"}]}""";
        var fromElement = FakeClient.Returning("response-noul.json");
        var fromUtf8 = FakeClient.Returning("response-noul.json");
        using var document = JsonDocument.Parse(json);

        var elementResult = await ((IJevClient)fromElement).EvaluateAsync<UrgencyCheck>(document.RootElement);
        var utf8Result = await ((IJevClient)fromUtf8).EvaluateUtf8Async<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.True(elementResult.IsSuccess);
        Assert.True(utf8Result.IsSuccess);
        Assert.Equal(elementResult.Value, utf8Result.Value);
        Assert.True(fromElement.OnlyRequest().State.TryGetJson(out var elementState));
        Assert.True(fromUtf8.OnlyRequest().State.TryGetJson(out var utf8State));
        Assert.True(JsonElement.DeepEquals(elementState, utf8State));
        Assert.True(JsonElement.DeepEquals(document.RootElement, utf8State));
        AssertQuestionsEqual(UrgencyCheck.QuestionsUtf8, fromUtf8.Requests[0]);
    }

    [Fact]
    public async Task JsonStringState_IsSentAsText()
    {
        var fake = FakeClient.Returning("response-noul.json");

        await ((IJevClient)fake).EvaluateUtf8Async<UrgencyCheck>("\"plain text\""u8.ToArray());

        Assert.True(fake.OnlyRequest().State.TryGetString(out var state));
        Assert.Equal("plain text", state);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("1 2")]
    [InlineData("{} {}")]
    [InlineData("  ")]
    public async Task Utf8_ThatIsNotASingleJsonValue_ThrowsBeforeCallingTheClient(string json)
    {
        var fake = FakeClient.Returning("response-noul.json");

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await ((IJevClient)fake).EvaluateUtf8Async<UrgencyCheck>(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("utf8JsonState", exception.ParamName);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Utf8_NumberState_ThrowsBeforeCallingTheClient()
    {
        var fake = FakeClient.Returning("response-noul.json");

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await ((IJevClient)fake).EvaluateUtf8Async<UrgencyCheck>("42"u8.ToArray()));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task NullString_Throws()
    {
        var fake = FakeClient.Returning("response-noul.json");

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await ((IJevClient)fake).EvaluateAsync<UrgencyCheck>((string)null!));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task UndefinedJsonElement_Throws()
    {
        var fake = FakeClient.Returning("response-noul.json");

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await ((IJevClient)fake).EvaluateAsync<UrgencyCheck>(default(JsonElement)));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task TypedState_IsSerializedThroughItsTypeInfo()
    {
        var fake = FakeClient.Returning("response-noul.json");
        var ticket = new TicketContext("Payouts failing", "Help! My payouts have been failing for 3 days.");

        var result = await ((IJevClient)fake).EvaluateAsync<TicketUrgency, TicketContext>(
            ticket, TicketContextJsonContext.Default.TicketContext);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        var request = fake.OnlyRequest();
        Assert.True(request.State.TryGetJson(out var state));
        using var expected = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(ticket, TicketContextJsonContext.Default.TicketContext));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, state));
        Assert.Equal(JevDefaults.Model, request.Model);
        AssertQuestionsEqual(TicketUrgency.QuestionsUtf8, request);
    }

    [Fact]
    public async Task TypedState_NullArguments_Throw()
    {
        var fake = FakeClient.Returning("response-noul.json");
        var ticket = new TicketContext("s", "b");

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await ((IJevClient)fake).EvaluateAsync<TicketUrgency, TicketContext>(null!, TicketContextJsonContext.Default.TicketContext));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await ((IJevClient)fake).EvaluateAsync<TicketUrgency, TicketContext>(ticket, null!));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task ChoiceAndScore_ReturnTypedAnswers()
    {
        var routing = await ((IJevClient)FakeClient.Returning("response-choice.json")).EvaluateAsync<DepartmentRouting>("text");
        var frustration = await ((IJevClient)FakeClient.Returning("response-score.json")).EvaluateAsync<FrustrationCheck>("text");

        Assert.Equal(Department.Billing, routing.Value.Department.Value);
        Assert.Equal(0.88, routing.Value.Department.Probabilities[Department.Billing]);
        Assert.Equal(Frustration.Frustrated, frustration.Value.Frustration.Value);
        Assert.Equal(0.92, frustration.Value.Frustration.Confidence);
    }

    [Fact]
    public async Task FailedResponse_ReturnsTheSameError()
    {
        var error = new JevError(JevErrorKind.RateLimited, "Slow down.") { StatusCode = 429 };
        IJevClient client = new FakeClient(Result<SystemOneResponse, JevError>.Failure(error));

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task AnswersTheParserRejects_GiveInvalidResponse_WithTheJsonException()
    {
        IJevClient client = FakeClient.Returning("response-choice.json");

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.IsType<JsonException>(result.Error.Exception);
    }

    [Fact]
    public async Task ResponseWithNullAnswers_GivesInvalidResponse()
    {
        IJevClient client = FakeClient.WithAnswers(null!);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.IsType<JsonException>(result.Error.Exception);
    }

    [Fact]
    public async Task ResponseWithEmptyAnswers_GivesInvalidResponse()
    {
        IJevClient client = FakeClient.WithAnswers(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.IsType<JsonException>(result.Error.Exception);
    }

    private static void AssertQuestionsEqual(ReadOnlySpan<byte> expectedQuestions, SystemOneRequest request)
    {
        var expected = JsonNode.Parse(expectedQuestions.ToArray());
        var actual = JsonNode.Parse(JsonSerializer.Serialize(request, JevJsonContext.Default.SystemOneRequest))!["questions"];
        Assert.True(JsonNode.DeepEquals(expected, actual), actual?.ToJsonString());
    }

    /// <summary>Implements only the two abstract members, so every typed call runs the default interface methods.</summary>
    private sealed class FakeClient(Result<SystemOneResponse, JevError> result) : IJevClient
    {
        public List<SystemOneRequest> Requests { get; } = [];

        public CancellationToken LastToken { get; private set; }

        public SystemOneRequest OnlyRequest()
        {
            // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
            return Assert.Single(Requests);
#pragma warning restore HLQ005
        }

        public static FakeClient Returning(string fixture)
            => new(Result<SystemOneResponse, JevError>.Success(
                JsonSerializer.Deserialize(Fixture.Text(fixture), JevJsonContext.Default.SystemOneResponse)!));

        public static FakeClient WithAnswers(IReadOnlyDictionary<string, JevAnswer> answers)
            => new(Result<SystemOneResponse, JevError>.Success(new SystemOneResponse
            {
                Model = "jev-1.13.0",
                Answers = answers,
                Usage = new JevUsage { InputTokens = 1, OutputTokens = 1 },
            }));

        public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            LastToken = ct;
            return ValueTask.FromResult(result);
        }

        public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
