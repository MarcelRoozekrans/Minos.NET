using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using ZeroAlloc.Results;
using MinosClient = Minos.IDecisionClient;
using MinosMetadata = Minos.DecisionClientMetadata;
using MinosRequest = Minos.DecisionRequest;
using MinosResponse = Minos.DecisionResponse;
using UpstreamClient = Microsoft.Extensions.AI.IDecisionClient;
using UpstreamMetadata = Microsoft.Extensions.AI.DecisionClientMetadata;
using UpstreamRequest = Microsoft.Extensions.AI.DecisionRequest;
using UpstreamResponse = Microsoft.Extensions.AI.DecisionResponse;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// <see cref="MinosDecisionClient"/>, the upstream client over a Minos client, against a hand-written Minos fake, per
/// the client, request, answers, errors and metadata tables of docs/extensions-ai-decisions/mapping.md.
/// </summary>
public sealed class MinosDecisionClientTests
{
    private const string Secret = "STATE-MARKER-7f3a";

    // Upstream questions with level ids of their own, not Minos's position keys.
    private static readonly DecisionQuestion[] Questions =
    [
        new BinaryDecisionQuestion("urgent", "Is it urgent?"),
        new ChoiceDecisionQuestion("team", "Which team?", [new("billing", "Payments"), new("tech", "Bugs")]),
        new ScoreDecisionQuestion("priority", "How soon?", [new("later", "Later"), new("today", "Today")]),
    ];

    private static UpstreamRequest Request(string state = "ticket text") => new(Text(state), Questions);

    private static JsonElement Text(string text) => JsonSerializer.SerializeToElement(text);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static QuestionAnswer[] GoodAnswers() =>
    [
        QuestionAnswer.Noul(0.8, ProbabilitySource.Logprobs),
        QuestionAnswer.Choice(1, 0.7, [0.3, 0.7]),
        QuestionAnswer.Score(1, 0.6, 0.6, [0.4, 0.6]),
    ];

    // ---- Sending ----

    [Fact]
    public async Task UpstreamQuestions_BecomeANewQuestionSetDefinition_OnEachCall()
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);

        await client.GetResponseAsync(Request(), new DecisionOptions { ModelId = "model-a" });
        await client.GetResponseAsync(Request());

        Assert.Equal(2, minos.Requests.Count);
        var first = minos.Requests[0].Request;
        Assert.Equal(
            [("urgent", QuestionKind.Noul), ("team", QuestionKind.Choice), ("priority", QuestionKind.Score)],
            first.Definition.Questions.Select(q => (q.Key, q.Kind)));
        Assert.Equal(["billing", "tech"], first.Definition.Questions[1].Options.Select(o => o.Key));
        Assert.Equal(["0", "1"], first.Definition.Questions[2].Options.Select(o => o.Key));
        Assert.NotSame(first.Definition, minos.Requests[1].Request.Definition);
        Assert.Equal("model-a", first.Model);
        Assert.Null(minos.Requests[1].Request.Model);
        Assert.Equal(0, first.RetryAttempt);
        Assert.Empty(first.Images);
        Assert.Null(first.Deadline);
    }

    [Fact]
    public async Task AStringState_IsSentAsText_AndAnObjectState_AsJson()
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);
        var json = Json("""{"id":7,"tags":["a"]}""");

        await client.GetResponseAsync(Request("ticket text"));
        await client.GetResponseAsync(new UpstreamRequest(json, Questions));

        Assert.True(minos.Requests[0].Request.State.TryGetString(out var text));
        Assert.Equal("ticket text", text);
        Assert.True(minos.Requests[1].Request.State.TryGetJson(out var sent));
        Assert.True(JsonElement.DeepEquals(json, sent));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public async Task ANumberBooleanOrNullState_FaultsTheTaskWithANonTransientClientException_WithoutCallingMinos(string state)
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);

        // The call returns a task rather than throwing; the task carries the exception.
        var task = client.GetResponseAsync(new UpstreamRequest(Json(state), Questions));
        var exception = await Assert.ThrowsAsync<DecisionClientException>(() => task);

        Assert.False(exception.IsTransient);
        Assert.DoesNotContain(state, exception.Message, StringComparison.Ordinal);
        Assert.Empty(minos.Requests);
    }

    [Fact]
    public async Task TheCancellationToken_IsPassedToTheMinosClient_AndCancellationPassesThrough()
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);
        using var cts = new CancellationTokenSource();

        await client.GetResponseAsync(Request(), cancellationToken: cts.Token);
        Assert.Equal(cts.Token, Assert.Single(minos.Requests).Token);

        minos.Throw = new OperationCanceledException(cts.Token);
        await cts.CancelAsync();
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(Request(), cancellationToken: cts.Token));
        Assert.Same(minos.Throw, thrown);
    }

    [Fact]
    public void ANullRequest_ThrowsSynchronously()
    {
        using var client = new MinosDecisionClient(new FakeMinosClient());

        // Thrown by the call itself, before a task exists, as the Minos clients do for a null argument.
        Assert.Throws<ArgumentNullException>(() => { _ = client.GetResponseAsync(null!); });
    }

    // ---- Answers and the response ----

    [Fact]
    public async Task TheAnswers_DeclareTwoDecimalPlaces_AndUseTheUpstreamIds()
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);
        var request = Request();

        var response = await client.GetResponseAsync(request);

        Assert.Same(request, response.Request);
        Assert.All(response.Answers, a => Assert.Equal(DecisionPrecision.TwoDecimalPlaces, a.Precision));
        Assert.Equal(0.8, Assert.IsType<BinaryDecisionAnswer>(response.Answers[0]).TrueProbability);
        Assert.Equal("tech", Assert.IsType<ChoiceDecisionAnswer>(response.Answers[1]).SelectedCandidateId);
        var score = Assert.IsType<ScoreDecisionAnswer>(response.Answers[2]);
        Assert.Equal(["later", "today"], score.Probabilities.Select(p => p.Id));
        Assert.Equal(0.6, score.Score);
        Assert.Equal(MinosPropertyKeys.Logprobs, response.Answers[0].AdditionalProperties?[MinosPropertyKeys.ProbabilitySource]);
    }

    [Fact]
    public async Task TheResponse_CarriesProvenanceUsageAndTheMinosResponse()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (request, definition) => new MinosResponse(definition, GoodAnswers(), "model-b", new DecisionUsage { InputTokens = 10, OutputTokens = 4 })
            {
                Id = "resp-1",
            },
        };
        using var client = new MinosDecisionClient(minos);

        var response = await client.GetResponseAsync(Request());

        var provenance = response.Provenance;
        Assert.NotNull(provenance);
        Assert.Equal("fake", provenance.ProviderName);
        Assert.Equal(new Uri("https://fake.example/"), provenance.ProviderUri);
        Assert.Equal("model-b", provenance.ModelId);
        Assert.Equal("resp-1", provenance.ResponseId);
        Assert.Equal(DecisionPrecision.TwoDecimalPlaces, provenance.Precision);
        Assert.Equal(10, response.Usage?.InputTokenCount);
        Assert.Equal(4, response.Usage?.OutputTokenCount);
        Assert.Equal(14, response.Usage?.TotalTokenCount);
        Assert.Same(minos.LastResponse, response.RawRepresentation);
    }

    [Fact]
    public async Task OneTokenCount_LeavesTheTotalEmpty_AndNoUsage_LeavesUsageEmpty()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers(), usage: new DecisionUsage { InputTokens = 10 }),
        };
        using var client = new MinosDecisionClient(minos);

        var response = await client.GetResponseAsync(Request());
        Assert.Equal(10, response.Usage?.InputTokenCount);
        Assert.Null(response.Usage?.OutputTokenCount);
        Assert.Null(response.Usage?.TotalTokenCount);

        minos.Respond = (_, definition) => new MinosResponse(definition, GoodAnswers());
        Assert.Null((await client.GetResponseAsync(Request())).Usage);
    }

    [Fact]
    public async Task TheMinosResponseValues_AreWrittenUnderTheMinosKeys()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers(), usage: new DecisionUsage { Cost = 0.25 })
            {
                UpstreamProvider = "host-a",
                IsCached = true,
                Path = FullPath(definition),
            },
        };
        using var client = new MinosDecisionClient(minos);

        var properties = (await client.GetResponseAsync(Request())).AdditionalProperties;

        Assert.NotNull(properties);
        Assert.Equal("host-a", Assert.IsType<string>(properties[MinosPropertyKeys.UpstreamProvider]));
        Assert.Equal(0.25, Assert.IsType<double>(properties[MinosPropertyKeys.Cost]));
        Assert.True(Assert.IsType<bool>(properties[MinosPropertyKeys.IsCached]));
        Assert.Equal(JsonValueKind.Object, Assert.IsType<JsonElement>(properties[MinosPropertyKeys.Path]).ValueKind);
        Assert.All(properties.Keys, k => Assert.StartsWith("minos.", k, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePath_IsWrittenAsJson_WithCamelCaseNamesAndNoNullMembers()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers()) { Path = FullPath(definition) },
        };
        using var client = new MinosDecisionClient(minos);

        var path = Assert.IsType<JsonElement>((await client.GetResponseAsync(Request()))!.AdditionalProperties![MinosPropertyKeys.Path]);

        Assert.Equal(ExpectedPathJson, path.GetRawText());
    }

    [Fact]
    public async Task TheResponse_SerializesWithTheUpstreamSourceGeneratedContracts_WithoutReflection()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers(), usage: new DecisionUsage { Cost = 0.25 })
            {
                UpstreamProvider = "host-a",
                IsCached = true,
                Path = FullPath(definition),
            },
        };
        using var client = new MinosDecisionClient(minos);
        var response = await client.GetResponseAsync(Request());

        // The default options without the reflection fallback they add when reflection is enabled, as under AOT.
        var sourceGenerated = AIJsonUtilities.DefaultOptions.TypeInfoResolverChain.OfType<JsonSerializerContext>().Single();
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { TypeInfoResolver = sourceGenerated };
        Assert.DoesNotContain(options.TypeInfoResolverChain, r => r is DefaultJsonTypeInfoResolver);

        var json = JsonSerializer.Serialize(response, (JsonTypeInfo<UpstreamResponse>)options.GetTypeInfo(typeof(UpstreamResponse)));

        using var document = JsonDocument.Parse(json);
        var properties = document.RootElement.GetProperty("additionalProperties");
        Assert.Equal("host-a", properties.GetProperty(MinosPropertyKeys.UpstreamProvider).GetString());
        Assert.Equal(0.25, properties.GetProperty(MinosPropertyKeys.Cost).GetDouble());
        Assert.True(properties.GetProperty(MinosPropertyKeys.IsCached).GetBoolean());
        Assert.True(JsonElement.DeepEquals(Json(ExpectedPathJson), properties.GetProperty(MinosPropertyKeys.Path)));
    }

    [Fact]
    public async Task ANonFiniteCallCost_IsWrittenAsANamedString()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers())
            {
                Path = new DecisionPath(
                    definition.Questions.Select(q => new QuestionRoute(q.Key, "a", RouteReason.Confident, 0.9, null)).ToArray(),
                    [
                        new DecisionCall("a", null, new DecisionUsage { Cost = double.NaN }, IsCached: false),
                        new DecisionCall("b", null, new DecisionUsage { Cost = double.PositiveInfinity }, IsCached: false),
                        new DecisionCall("c", null, new DecisionUsage { Cost = double.NegativeInfinity }, IsCached: false),
                    ]),
            },
        };
        using var client = new MinosDecisionClient(minos);

        var path = Assert.IsType<JsonElement>((await client.GetResponseAsync(Request())).AdditionalProperties![MinosPropertyKeys.Path]);

        Assert.Equal(["NaN", "Infinity", "-Infinity"], path.GetProperty("calls").EnumerateArray().Select(c => c.GetProperty("cost").GetString()));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-double.Epsilon)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task OverMinos_RefusesANegativeOrNonFiniteCost_AsAnInvalidResponse(double cost)
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers(), usage: new DecisionUsage { Cost = cost }),
        };
        using var client = new MinosDecisionClient(minos);

        var exception = await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(Request(Secret)));

        Assert.Contains("cost", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cost.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
        var error = Assert.IsType<MinosDecisionException>(exception.InnerException).Error;
        Assert.NotNull(error);
        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(exception.Message, error.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    public async Task AValidCost_IsWrittenAndReadBack(double cost)
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers(), usage: new DecisionUsage { Cost = cost }),
        };
        using var upstream = new MinosDecisionClient(minos);
        Assert.Equal(cost, Assert.IsType<double>((await upstream.GetResponseAsync(Request())).AdditionalProperties![MinosPropertyKeys.Cost]));

        using var back = upstream.AsMinosDecisionClient();
        var result = await back.EvaluateAsync(new MinosRequest(
            new QuestionSetDefinition(
                QuestionDefinition.Noul("urgent", "Is it urgent?"),
                QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", "Bugs")),
                QuestionDefinition.Score("priority", "How soon?", "Later", "Today")),
            "s"));

        Assert.True(result.IsSuccess);
        Assert.Equal(cost, result.Value.Usage?.Cost);
    }

    private const string ExpectedPathJson =
        """{"routes":[{"key":"urgent","answeredBy":"primary","reason":"confident","primaryCertainty":0.9},{"key":"team","answeredBy":"target","reason":"escalated","primaryCertainty":0.4,"targetCertainty":0.8},{"key":"priority","answeredBy":"backup","reason":"fellBack","primaryCertainty":0.5}],"calls":[{"clientName":"primary","model":"model-b","isCached":false,"status":"answered","inputTokens":12,"outputTokens":3,"cost":0.001},{"clientName":"target","isCached":true,"status":"failed","errorKind":"rateLimited"},{"clientName":"backup","isCached":false,"status":"notCalledNoTimeLeft"}]}""";

    private static DecisionPath FullPath(QuestionSetDefinition definition) => new(
        [
            new QuestionRoute(definition.Questions[0].Key, "primary", RouteReason.Confident, 0.9, null),
            new QuestionRoute(definition.Questions[1].Key, "target", RouteReason.Escalated, 0.4, 0.8),
            new QuestionRoute(definition.Questions[2].Key, "backup", RouteReason.FellBack, 0.5, null),
        ],
        [
            new DecisionCall("primary", "model-b", new DecisionUsage { InputTokens = 12, OutputTokens = 3, Cost = 0.001 }, IsCached: false),
            new DecisionCall("target", null, null, IsCached: true) { Status = CallStatus.Failed, ErrorKind = DecisionErrorKind.RateLimited },
            new DecisionCall("backup", null, null, IsCached: false) { Status = CallStatus.NotCalledNoTimeLeft },
        ]);

    [Fact]
    public async Task MissingMinosResponseValues_AreLeftOut_AndIsCachedIsAlwaysWritten()
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);

        var properties = (await client.GetResponseAsync(Request())).AdditionalProperties;

        Assert.NotNull(properties);
        Assert.False(Assert.IsType<bool>(Assert.Single(properties, p => string.Equals(p.Key, MinosPropertyKeys.IsCached, StringComparison.Ordinal)).Value));
        Assert.Single(properties);
    }

    [Fact]
    public async Task OverUpstream_ReadsBackTheValuesOverMinosWrites()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, GoodAnswers(), "model-b", new DecisionUsage { InputTokens = 3, Cost = 0.5 })
            {
                Id = "resp-2",
                UpstreamProvider = "host-a",
                IsCached = true,
            },
        };
        using var roundTrip = minos.AsUpstreamDecisionClient().AsMinosDecisionClient();
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Noul("urgent", "Is it urgent?"),
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", "Bugs")),
            QuestionDefinition.Score("priority", "How soon?", "Later", "Today"));

        var result = await roundTrip.EvaluateAsync(new MinosRequest(definition, "s"));

        Assert.True(result.IsSuccess);
        var back = result.Value;
        Assert.Equal("host-a", back.UpstreamProvider);
        Assert.True(back.IsCached);
        Assert.Equal(0.5, back.Usage?.Cost);
        Assert.Equal(3, back.Usage?.InputTokens);
        Assert.Equal("model-b", back.Model);
        Assert.Equal("resp-2", back.Id);
        Assert.Equal(ProbabilitySource.Logprobs, back.Answers[0].ProbabilitySource);
        Assert.Equal(1, back.Answers[1].ChosenIndex);
        Assert.Equal(0.6, back.Answers[2].Expected);
    }

    // ---- Errors ----

    [Theory]
    [InlineData(DecisionErrorKind.RateLimited, null, true)]
    [InlineData(DecisionErrorKind.Overloaded, null, true)]
    [InlineData(DecisionErrorKind.Server, 500, true)]
    [InlineData(DecisionErrorKind.Network, null, true)]
    [InlineData(DecisionErrorKind.Timeout, null, true)]
    [InlineData(DecisionErrorKind.Http, 408, true)]
    [InlineData(DecisionErrorKind.Http, 400, false)]
    [InlineData(DecisionErrorKind.Unauthorized, 401, false)]
    [InlineData(DecisionErrorKind.Validation, 422, false)]
    [InlineData(DecisionErrorKind.Refused, null, false)]
    [InlineData(DecisionErrorKind.Unsupported, null, false)]
    [InlineData(DecisionErrorKind.InvalidQuestions, null, false)]
    [InlineData(DecisionErrorKind.Disposed, null, false)]
    public async Task ADecisionError_ThrowsAClientException_TransientAsMinosClassifiesIt(DecisionErrorKind kind, int? status, bool transient)
    {
        var cause = new InvalidOperationException("cause");
        var error = new DecisionError(kind, "The provider failed.") { StatusCode = status, RequestId = "req-1", Exception = cause };
        using var minos = new FakeMinosClient { Error = error };
        using var client = new MinosDecisionClient(minos);

        var exception = await Assert.ThrowsAsync<DecisionClientException>(() => client.GetResponseAsync(Request(Secret)));

        Assert.Equal("The provider failed.", exception.Message);
        Assert.Equal(transient, exception.IsTransient);
        Assert.Equal(DecisionRetryOptions.IsTransient(error), exception.IsTransient);
        var inner = Assert.IsType<MinosDecisionException>(exception.InnerException);
        Assert.Same(error, inner.Error);
        Assert.Equal(error.Message, inner.Message);
        Assert.Same(cause, inner.InnerException);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidResponseError_ThrowsAProtocolException_WithTheErrorInside()
    {
        var error = new DecisionError(DecisionErrorKind.InvalidResponse, "The provider's reply broke the protocol.") { StatusCode = 200, RequestId = "req-2" };
        using var minos = new FakeMinosClient { Error = error };
        using var client = new MinosDecisionClient(minos);

        var exception = await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(Request(Secret)));

        Assert.Equal(error.Message, exception.Message);
        var inner = Assert.IsType<MinosDecisionException>(exception.InnerException);
        Assert.Same(error, inner.Error);
        Assert.Equal(200, inner.Error?.StatusCode);
        Assert.Equal("req-2", inner.Error?.RequestId);
        Assert.Null(inner.InnerException);
    }

    [Fact]
    public async Task ADistributionUpstreamRefuses_ThrowsAProtocolException_WithAnInvalidResponseErrorInside()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, definition) => new MinosResponse(definition, [
                QuestionAnswer.Noul(0.8),
                QuestionAnswer.Choice(0, 0.5, [0.5, 0.3]),
                QuestionAnswer.Score(1, 0.6, 0.6, [0.4, 0.6]),
            ]),
        };
        using var client = new MinosDecisionClient(minos);

        var exception = await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(Request(Secret)));

        var inner = Assert.IsType<MinosDecisionException>(exception.InnerException);
        var error = inner.Error;
        Assert.NotNull(error);
        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(exception.Message, error.Message);
        var caught = Assert.IsType<DecisionProtocolException>(error.Exception);
        Assert.Equal(exception.Message, caught.Message);
        Assert.NotSame(caught, exception);
        Assert.Same(caught, inner.InnerException);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnswersThatDoNotFitTheQuestions_ThrowAProtocolException()
    {
        using var minos = new FakeMinosClient
        {
            Respond = (_, _) => new MinosResponse(new QuestionSetDefinition(QuestionDefinition.Noul("urgent", "Is it urgent?")), [QuestionAnswer.Noul(0.8)]),
        };
        using var client = new MinosDecisionClient(minos);

        var exception = await Assert.ThrowsAsync<DecisionProtocolException>(() => client.GetResponseAsync(Request()));

        Assert.Equal(DecisionErrorKind.InvalidResponse, Assert.IsType<MinosDecisionException>(exception.InnerException).Error?.Kind);
    }

    [Fact]
    public async Task AnswersInAnotherOrderThanTheQuestions_ThrowAProtocolException_WithAnInvalidResponseErrorInside()
    {
        DecisionQuestion[] questions = [new BinaryDecisionQuestion("first", "First?"), new BinaryDecisionQuestion("second", "Second?")];

        // Same kinds and count, so only the keys tell that the Minos client answered the questions in swapped order.
        using var minos = new FakeMinosClient
        {
            Respond = (_, _) => new MinosResponse(
                new QuestionSetDefinition(QuestionDefinition.Noul("second", "Second?"), QuestionDefinition.Noul("first", "First?")),
                [QuestionAnswer.Noul(0.9), QuestionAnswer.Noul(0.1)]),
        };
        using var client = new MinosDecisionClient(minos);

        var exception = await Assert.ThrowsAsync<DecisionProtocolException>(
            () => client.GetResponseAsync(new UpstreamRequest(Text("ticket text"), questions)));

        var inner = Assert.IsType<MinosDecisionException>(exception.InnerException);
        Assert.Equal(DecisionErrorKind.InvalidResponse, inner.Error?.Kind);
        Assert.Equal("The answers do not match the questions they belong to.", exception.Message);
        Assert.DoesNotContain("first", exception.Message, StringComparison.Ordinal);
    }

    // ---- Metadata, services and disposal ----

    [Fact]
    public void GetService_ReturnsUpstreamMetadata_BuiltFromTheMinosMetadataAndCapabilities()
    {
        using var minos = new FakeMinosClient { Kinds = QuestionKinds.Noul | QuestionKinds.Score };
        using var client = new MinosDecisionClient(minos);

        var metadata = Assert.IsType<UpstreamMetadata>(client.GetService(typeof(UpstreamMetadata)));

        Assert.Equal("fake", metadata.ProviderName);
        Assert.Equal(new Uri("https://fake.example/"), metadata.ProviderUri);
        Assert.Equal("model-default", metadata.DefaultModelId);
        Assert.Equal([DecisionKind.Binary, DecisionKind.Score], metadata.SupportedKinds);
        Assert.Same(metadata, client.GetService(typeof(UpstreamMetadata)));
    }

    [Fact]
    public void AMinosClientWithoutMetadataOrCapabilities_GivesUpstreamMetadataWithUnknownValues()
    {
        using var minos = new FakeMinosClient { NoServices = true };
        using var client = new MinosDecisionClient(minos);

        var metadata = Assert.IsType<UpstreamMetadata>(client.GetService(typeof(UpstreamMetadata)));

        Assert.Null(metadata.ProviderName);
        Assert.Null(metadata.ProviderUri);
        Assert.Null(metadata.DefaultModelId);
        Assert.Null(metadata.SupportedKinds);
    }

    [Fact]
    public void GetService_ReturnsItselfTheWrappedClientAndTheMinosServices()
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);

        Assert.Same(client, client.GetService(typeof(MinosDecisionClient)));
        Assert.Same(minos, client.GetService(typeof(MinosClient)));
        Assert.Same(minos, client.GetService(typeof(FakeMinosClient)));
        Assert.Equal(QuestionKinds.All, Assert.IsType<DecisionCapabilities>(client.GetService(typeof(DecisionCapabilities))).QuestionKinds);
        Assert.IsType<MinosMetadata>(client.GetService(typeof(MinosMetadata)));
        Assert.Null(client.GetService(typeof(string)));
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
    }

    [Fact]
    public void AKeyedLookup_GoesToTheMinosClientOnly()
    {
        using var minos = new FakeMinosClient();
        using var client = new MinosDecisionClient(minos);

        Assert.Null(client.GetService(typeof(UpstreamMetadata), "key"));
        Assert.Null(client.GetService(typeof(MinosDecisionClient), "key"));
        Assert.Equal(["key", "key"], minos.KeyedLookups);
    }

    [Fact]
    public async Task Dispose_DisposesTheMinosClientOnce_AndACallAfterItThrows()
    {
        var minos = new FakeMinosClient();
        var client = new MinosDecisionClient(minos);

        client.Dispose();
        client.Dispose();

        Assert.Equal(1, minos.DisposeCount);

        // The call returns a task rather than throwing; the task carries the exception.
        var task = client.GetResponseAsync(Request());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => task);
        Assert.Empty(minos.Requests);
    }

    [Fact]
    public async Task ConcurrentDispose_DisposesTheMinosClientOnce()
    {
        for (var round = 0; round < 50; round++)
        {
            var minos = new FakeMinosClient();
            var client = new MinosDecisionClient(minos);
            using var start = new Barrier(8);

            var disposers = new Task[8];
            for (var i = 0; i < disposers.Length; i++)
            {
                disposers[i] = Task.Run(() =>
                {
                    start.SignalAndWait();
                    client.Dispose();
                });
            }

            await Task.WhenAll(disposers);

            Assert.Equal(1, minos.DisposeCount);
        }
    }

    [Fact]
    public void TheConstructor_RejectsANullClient()
        => Assert.Throws<ArgumentNullException>(() => new MinosDecisionClient(null!));

    // ---- Entry points ----

    [Fact]
    public void AsUpstreamDecisionClient_WrapsAMinosClientInAnUpstreamClient()
    {
        using var minos = new FakeMinosClient();

        using var upstream = minos.AsUpstreamDecisionClient();

        Assert.IsAssignableFrom<UpstreamClient>(upstream);
        Assert.Same(minos, upstream.GetService(typeof(MinosClient)));
        Assert.Throws<ArgumentNullException>(() => ((MinosClient)null!).AsUpstreamDecisionClient());
    }

    [Fact]
    public void AsMinosDecisionClient_WrapsAnUpstreamClientInAMinosClient()
    {
        using var minos = new FakeMinosClient();
        using var upstream = new MinosDecisionClient(minos);

        using var back = upstream.AsMinosDecisionClient();

        Assert.IsAssignableFrom<MinosClient>(back);
        Assert.Throws<ArgumentNullException>(() => ((UpstreamClient)null!).AsMinosDecisionClient());
    }

    // ---- MinosDecisionException ----

    [Fact]
    public void MinosDecisionException_CarriesTheError_AndHasTheStandardConstructors()
    {
        var cause = new InvalidOperationException("cause");
        var error = new DecisionError(DecisionErrorKind.Server, "Down.") { Exception = cause };

        var carrying = new MinosDecisionException(error);
        Assert.Same(error, carrying.Error);
        Assert.Equal("Down.", carrying.Message);
        Assert.Same(cause, carrying.InnerException);
        Assert.Throws<ArgumentNullException>(() => new MinosDecisionException((DecisionError)null!));

        Assert.Null(new MinosDecisionException().Error);
        Assert.Equal("m", new MinosDecisionException("m").Message);
        var wrapped = new MinosDecisionException("m", cause);
        Assert.Same(cause, wrapped.InnerException);
        Assert.Null(wrapped.Error);
    }

    /// <summary>A Minos client that records its calls and answers with <see cref="Respond"/> or fails with <see cref="Error"/>.</summary>
    private sealed class FakeMinosClient : MinosClient
    {
        private readonly MinosMetadata _metadata = new("fake", new Uri("https://fake.example/"), "model-default");

        public List<(MinosRequest Request, CancellationToken Token)> Requests { get; } = [];

        public List<object> KeyedLookups { get; } = [];

        public Func<MinosRequest, QuestionSetDefinition, MinosResponse> Respond { get; set; } =
            static (_, definition) => new MinosResponse(definition, GoodAnswers());

        public DecisionError? Error { get; set; }

        public Exception? Throw { get; set; }

        public QuestionKinds Kinds { get; set; } = QuestionKinds.All;

        public bool NoServices { get; set; }

        public MinosResponse? LastResponse { get; private set; }

        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask<Result<MinosResponse, DecisionError>> EvaluateAsync(MinosRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add((request, cancellationToken));
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Error is not null)
            {
                return new(Result<MinosResponse, DecisionError>.Failure(Error));
            }

            LastResponse = Respond(request, request.Definition);
            return new(Result<MinosResponse, DecisionError>.Success(LastResponse));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceKey is not null)
            {
                KeyedLookups.Add(serviceKey);
                return null;
            }

            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (NoServices)
            {
                return null;
            }

            if (serviceType == typeof(MinosMetadata))
            {
                return _metadata;
            }

            return serviceType == typeof(DecisionCapabilities)
                ? new DecisionCapabilities { QuestionKinds = Kinds, MaxQuestions = null, MaxChoiceOptions = null, SupportsImages = false, SupportsModelListing = false }
                : null;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
