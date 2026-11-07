using System.Text.Json;
using Microsoft.Extensions.AI;
using MinosMetadata = Minos.DecisionClientMetadata;
using MinosRequest = Minos.DecisionRequest;
using UpstreamClient = Microsoft.Extensions.AI.IDecisionClient;
using UpstreamMetadata = Microsoft.Extensions.AI.DecisionClientMetadata;
using UpstreamRequest = Microsoft.Extensions.AI.DecisionRequest;
using UpstreamResponse = Microsoft.Extensions.AI.DecisionResponse;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// <see cref="UpstreamDecisionClient"/>, the Minos client over an upstream client, against a hand-written upstream fake,
/// per the client, request, errors and metadata tables of docs/extensions-ai-decisions/mapping.md.
/// </summary>
public sealed class UpstreamDecisionClientTests
{
    private const string Secret = "STATE-MARKER-7f3a";

    private static readonly QuestionSetDefinition Definition = new(
        QuestionDefinition.Noul("urgent", "Is it urgent?"),
        QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", null)),
        QuestionDefinition.Score("priority", "How soon?", "Later", "Today"));

    // ---- Sending ----

    [Fact]
    public async Task ARequest_IsSentWithTheConvertedQuestionsStateAndModel()
    {
        using var upstream = new FakeUpstreamClient();
        using var client = new UpstreamDecisionClient(upstream);

        var result = await client.EvaluateAsync(new MinosRequest(Definition, "ticket text") { Model = "model-a", RetryAttempt = 2 });

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(upstream.Requests);
        Assert.Equal(JsonValueKind.String, sent.Request.State.ValueKind);
        Assert.Equal("ticket text", sent.Request.State.GetString());
        Assert.Equal("model-a", sent.Options?.ModelId);
        Assert.Null(sent.Options?.AdditionalProperties);
        Assert.Null(sent.Options?.RawRepresentationFactory);

        Assert.Equal(
            [("urgent", DecisionKind.Binary), ("team", DecisionKind.Choice), ("priority", DecisionKind.Score)],
            sent.Request.Questions.Select(q => (q.Id, q.Kind)));
        var choice = Assert.IsType<ChoiceDecisionQuestion>(sent.Request.Questions[1]);
        Assert.Equal([("billing", "Payments"), ("tech", "tech")], choice.Candidates.Select(c => (c.Id, c.Description)));
        var score = Assert.IsType<ScoreDecisionQuestion>(sent.Request.Questions[2]);
        Assert.Equal(["0", "1"], score.Levels.Select(l => l.Id));
    }

    [Fact]
    public async Task JsonState_IsSentAsItIs_AndNoModel_SendsNoModelId()
    {
        using var upstream = new FakeUpstreamClient();
        using var client = new UpstreamDecisionClient(upstream);
        using var json = JsonDocument.Parse("""{"id":7,"tags":["a"]}""");

        var result = await client.EvaluateAsync(new MinosRequest(Definition, DecisionContent.FromJson(json.RootElement)));

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(upstream.Requests);
        Assert.True(JsonElement.DeepEquals(json.RootElement, sent.Request.State));
        Assert.Null(sent.Options?.ModelId);
    }

    [Fact]
    public async Task TheCancellationToken_IsPassedToTheUpstreamClient()
    {
        using var upstream = new FakeUpstreamClient();
        using var client = new UpstreamDecisionClient(upstream);
        using var cts = new CancellationTokenSource();

        await client.EvaluateAsync(new MinosRequest(Definition, "s"), cts.Token);

        Assert.Equal(cts.Token, Assert.Single(upstream.Requests).Token);
    }

    // ---- Answers and the response ----

    [Fact]
    public async Task UpstreamAnswers_ComeBackAsADecisionResponse_WithModelIdAndUsage()
    {
        using var upstream = new FakeUpstreamClient
        {
            Respond = (request, _) => new UpstreamResponse(
                request,
                [
                    new BinaryDecisionAnswer("urgent", 0.8),
                    new ChoiceDecisionAnswer("team", "tech", [new("billing", 0.3), new("tech", 0.7)]),
                    new ScoreDecisionAnswer("priority", 0.75, [new("0", 0.25), new("1", 0.75)]),
                ],
                new DecisionProvenance("prov", new Uri("https://example.com"), "model-b", "resp-1"),
                new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3, TotalTokenCount = 15 }),
        };
        using var client = new UpstreamDecisionClient(upstream);
        var request = new MinosRequest(Definition, "s");

        var response = (await client.EvaluateAsync(request)).Value;

        Assert.Same(request.Definition, response.Definition);
        Assert.Equal(("model-b", "resp-1"), (response.Model, response.Id));
        Assert.Equal(new DecisionUsage { InputTokens = 12, OutputTokens = 3 }, response.Usage);
        Assert.Null(response.UpstreamProvider);
        Assert.False(response.IsCached);
        Assert.Null(response.Path);

        var answers = response.Answers;
        Assert.Equal(0.8, answers[0].Probability);
        Assert.Equal((1, 0.7), (answers[1].ChosenIndex, answers[1].Confidence));
        Assert.Equal((1, 0.75), (answers[2].ChosenIndex, answers[2].Expected));
        Assert.Equal(ProbabilitySource.Model, response.ProbabilitySource);
    }

    [Fact]
    public async Task ATokenCountAboveIntMaxValue_IsLeftEmpty_AndNoUsageOrProvenance_IsNull()
    {
        using var upstream = new FakeUpstreamClient
        {
            Respond = (request, _) => FakeUpstreamClient.Answer(request, usage: new UsageDetails { InputTokenCount = (long)int.MaxValue + 1, OutputTokenCount = 4 }),
        };
        using var client = new UpstreamDecisionClient(upstream);

        var response = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Value;
        Assert.Equal(new DecisionUsage { OutputTokens = 4 }, response.Usage);

        upstream.Respond = (request, _) => FakeUpstreamClient.Answer(request);
        response = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Value;
        Assert.Null(response.Usage);
        Assert.Null(response.Model);
        Assert.Null(response.Id);
    }

    [Fact]
    public async Task TheMinosResponseKeys_AreReadBack_AsValuesAndAsJson()
    {
        var properties = new Dictionary<string, object?>
        {
            ["minos.upstreamProvider"] = "host-a",
            ["minos.cost"] = 0.25,
            ["minos.isCached"] = true,
            ["minos.path"] = "ignored",
            ["other"] = 1,
        };
        using var upstream = new FakeUpstreamClient { Respond = (request, _) => FakeUpstreamClient.Answer(request, properties: properties) };
        using var client = new UpstreamDecisionClient(upstream);

        var response = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Value;

        Assert.Equal("host-a", response.UpstreamProvider);
        Assert.Equal(new DecisionUsage { Cost = 0.25 }, response.Usage);
        Assert.True(response.IsCached);
        Assert.Null(response.Path);

        // After a JSON round trip the values are JsonElements.
        var json = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["minos.upstreamProvider"] = "host-b", ["minos.cost"] = 1.5, ["minos.isCached"] = false });
        var asJson = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        upstream.Respond = (request, _) => FakeUpstreamClient.Answer(request, usage: new UsageDetails { InputTokenCount = 2 }, properties: asJson);

        response = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Value;

        Assert.Equal("host-b", response.UpstreamProvider);
        Assert.Equal(new DecisionUsage { InputTokens = 2, Cost = 1.5 }, response.Usage);
        Assert.False(response.IsCached);
    }

    [Theory]
    [InlineData("minos.upstreamProvider", 0)]
    [InlineData("minos.cost", 1)]
    [InlineData("minos.cost", 2)]
    [InlineData("minos.isCached", 3)]
    [InlineData("minos.upstreamProvider", 4)]
    public async Task AMalformedMinosResponseKey_IsAnInvalidResponse_NamingTheKeyButNotTheValue(string key, int value)
    {
        object?[] values = [42, "cheap", -1.0, "yes", null];
        var properties = new Dictionary<string, object?> { [key] = values[value] };
        using var upstream = new FakeUpstreamClient { Respond = (request, _) => FakeUpstreamClient.Answer(request, properties: properties) };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, Secret))).Error;

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.Contains(key, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("42", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cheap", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerTheMapperRejects_IsAnInvalidResponse()
    {
        var properties = new Dictionary<string, object?> { ["minos.confidence"] = 3.0 };
        using var upstream = new FakeUpstreamClient
        {
            Respond = (request, _) => new UpstreamResponse(
                request,
                [
                    new BinaryDecisionAnswer("urgent", 0.8),
                    new ChoiceDecisionAnswer("team", "tech", [new("billing", 0.3), new("tech", 0.7)], additionalProperties: properties),
                    new ScoreDecisionAnswer("priority", 0.75, [new("0", 0.25), new("1", 0.75)]),
                ]),
        };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Error;

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
    }

    // ---- Response validation ----

    [Fact]
    public async Task AResponseForADifferentState_IsAnInvalidResponse()
    {
        using var upstream = new FakeUpstreamClient
        {
            Respond = (request, _) => FakeUpstreamClient.Answer(new UpstreamRequest(JsonSerializer.SerializeToElement("other"), request.Questions)),
        };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, Secret))).Error;

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.Null(error.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AResponseForDifferentQuestions_IsAnInvalidResponse(int change)
    {
        using var upstream = new FakeUpstreamClient
        {
            Respond = (request, _) =>
            {
                var questions = request.Questions.ToArray();
                switch (change)
                {
                    case 0:
                        questions[0] = new BinaryDecisionQuestion("urgent", "Is it really urgent?");
                        break;
                    case 1:
                        questions[0] = new BinaryDecisionQuestion("urgent", "Is it urgent?", "Yes", "No");
                        break;
                    case 2:
                        questions[1] = new ChoiceDecisionQuestion("team", "Which team?", [new("billing", "Payments"), new("tech", "Bugs")]);
                        break;
                    case 3:
                        questions[2] = new ScoreDecisionQuestion("priority", "How soon?", [new("0", "Later"), new("1", "Now")]);
                        break;
                    default:
                        questions = questions[..2];
                        break;
                }

                return FakeUpstreamClient.Answer(new UpstreamRequest(request.State, questions));
            },
        };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Error;

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
    }

    [Fact]
    public async Task AnEquivalentButNewRequest_IsAccepted()
    {
        using var upstream = new FakeUpstreamClient
        {
            Respond = (request, _) => FakeUpstreamClient.Answer(new UpstreamRequest(request.State.Clone(), [.. request.Questions])),
        };
        using var client = new UpstreamDecisionClient(upstream);

        Assert.True((await client.EvaluateAsync(new MinosRequest(Definition, "s"))).IsSuccess);
    }

    [Fact]
    public async Task ANullResponse_IsAnInvalidResponse()
    {
        using var upstream = new FakeUpstreamClient { Respond = (_, _) => null! };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Error;

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
    }

    // ---- Errors ----

    [Theory]
    [InlineData(0, DecisionErrorKind.InvalidResponse)]
    [InlineData(1, DecisionErrorKind.Server)]
    [InlineData(2, DecisionErrorKind.Http)]
    [InlineData(3, DecisionErrorKind.Http)]
    [InlineData(4, DecisionErrorKind.Http)]
    public async Task AnUpstreamException_BecomesADecisionError_WithItsMessageAndTheException(int which, DecisionErrorKind kind)
    {
        Exception[] exceptions =
        [
            new DecisionProtocolException("Choice probabilities must sum to 1."),
            new DecisionClientException("The provider is overloaded.", isTransient: true),
            new DecisionClientException("The provider rejected the key.", isTransient: false),
            new InvalidOperationException("Something broke."),
            new OperationCanceledException("A time-out the caller did not ask for."),
        ];
        var thrown = exceptions[which];
        using var upstream = new FakeUpstreamClient { Respond = (_, _) => throw thrown };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, Secret))).Error;

        Assert.Equal(kind, error.Kind);
        Assert.Equal(thrown.Message, error.Message);
        Assert.Same(thrown, error.Exception);
        Assert.Null(error.StatusCode);
        Assert.Null(error.RetryAfter);
        Assert.Null(error.Detail);
        Assert.Null(error.RequestId);
        Assert.Empty(error.Failures);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExceptionThrownSynchronously_IsMappedTheSameWay()
    {
        using var upstream = new FakeUpstreamClient { ThrowSynchronously = new DecisionClientException("down", isTransient: true) };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Error;

        Assert.Equal(DecisionErrorKind.Server, error.Kind);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceledException_Unchanged()
    {
        using var cts = new CancellationTokenSource();
        var thrown = new OperationCanceledException("cancelled", cts.Token);
        using var upstream = new FakeUpstreamClient
        {
            Respond = (_, _) =>
            {
                cts.Cancel();
                throw thrown;
            },
        };
        using var client = new UpstreamDecisionClient(upstream);

        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(new MinosRequest(Definition, "s"), cts.Token));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task AnAlreadyCancelledToken_ThrowsOperationCanceledException_WithoutSending()
    {
        using var upstream = new FakeUpstreamClient();
        using var client = new UpstreamDecisionClient(upstream);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(new MinosRequest(Definition, "s"), new CancellationToken(canceled: true)));

        Assert.Empty(upstream.Requests);
    }

    // ---- Refusals before sending ----

    [Fact]
    public async Task AnEmptyQuestionSet_IsAValidationError_AndNothingIsSent()
    {
        using var upstream = new FakeUpstreamClient { Metadata = new UpstreamMetadata("fake", supportedKinds: [DecisionKind.Binary]) };
        using var client = new UpstreamDecisionClient(upstream);

        // As the chat client adapter does: Validation before any capability rule, even with images, and no failures.
        var error = (await client.EvaluateAsync(new MinosRequest(new QuestionSetDefinition(), "s") { Images = [DecisionImage.FromBytes(new byte[] { 1 }, "image/png")] })).Error;

        Assert.Equal(DecisionErrorKind.Validation, error.Kind);
        Assert.Equal("The request has no questions.", error.Message);
        Assert.Empty(error.Failures);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task EveryPageRefusal_IsReported_AndNothingIsSent()
    {
        using var upstream = new FakeUpstreamClient();
        using var client = new UpstreamDecisionClient(upstream);
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("urgent", "Is it urgent?"), QuestionDefinition.Score("level", "How bad?", "Only"));
        var request = new MinosRequest(definition, Secret) { Images = [DecisionImage.FromBytes(new byte[] { 1 }, "image/png")] };

        var error = (await client.EvaluateAsync(request)).Error;

        Assert.Equal(DecisionErrorKind.Unsupported, error.Kind);
        Assert.Equal([("MIN206", null), ("MIN210", "level")], error.Failures.Select(f => (f.Rule, f.QuestionKey)));
        Assert.Equal(error.Failures[0].Message, error.Message);
        Assert.All(error.Failures, f => Assert.DoesNotContain(Secret, f.Message, StringComparison.Ordinal));
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AKindOutsideTheUpstreamSupportedKinds_IsUnsupportedWithMin201_AndNothingIsSent()
    {
        using var upstream = new FakeUpstreamClient { Metadata = new UpstreamMetadata("prov", supportedKinds: [DecisionKind.Binary, DecisionKind.Choice]) };
        using var client = new UpstreamDecisionClient(upstream);

        var error = (await client.EvaluateAsync(new MinosRequest(Definition, "s"))).Error;

        Assert.Equal(DecisionErrorKind.Unsupported, error.Kind);
        var failure = Assert.Single(error.Failures);
        Assert.Equal(("MIN201", "priority"), (failure.Rule, failure.QuestionKey));
        Assert.Equal("prov does not support Score questions.", failure.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task KindAndImageFailures_PrecedeThePageRefusals_AsInANativeClient()
    {
        using var upstream = new FakeUpstreamClient { Metadata = new UpstreamMetadata(supportedKinds: [DecisionKind.Choice]) };
        using var client = new UpstreamDecisionClient(upstream);
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("urgent", "Is it urgent?"), QuestionDefinition.Score("level", "How bad?", "Only"));
        var request = new MinosRequest(definition, "s") { Images = [DecisionImage.FromBytes(new byte[] { 1 }, "image/png")] };

        var error = (await client.EvaluateAsync(request)).Error;

        Assert.Equal(
            [("MIN201", "urgent"), ("MIN201", "level"), ("MIN206", null), ("MIN210", "level")],
            error.Failures.Select(f => (f.Rule, f.QuestionKey)));
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task NoSupportedKinds_AssumesEveryKind()
    {
        using var upstream = new FakeUpstreamClient { Metadata = new UpstreamMetadata("prov") };
        using var client = new UpstreamDecisionClient(upstream);

        Assert.True((await client.EvaluateAsync(new MinosRequest(Definition, "s"))).IsSuccess);
        Assert.Single(upstream.Requests);
    }

    // ---- Disposal ----

    [Fact]
    public async Task ADisposedClient_ThrowsObjectDisposedException_AndSendsNothing()
    {
        var upstream = new FakeUpstreamClient();
        var client = new UpstreamDecisionClient(upstream);
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(new MinosRequest(Definition, "s")));
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public void Dispose_DisposesTheUpstreamClientOnce()
    {
        var upstream = new FakeUpstreamClient();
        var client = new UpstreamDecisionClient(upstream);

        client.Dispose();
        client.Dispose();

        Assert.Equal(1, upstream.DisposeCount);
    }

    [Fact]
    public void ConcurrentDisposes_DisposeTheUpstreamClientOnce()
    {
        var upstream = new FakeUpstreamClient();
        var client = new UpstreamDecisionClient(upstream);

        Parallel.For(0, 64, _ => client.Dispose());

        Assert.Equal(1, upstream.DisposeCount);
    }

    [Fact]
    public async Task ADisposalWhileACallIsInFlight_ReturnsDisposed()
    {
        var gate = new TaskCompletionSource<UpstreamResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var upstream = new FakeUpstreamClient { RespondAsync = (_, _) => gate.Task };
        var client = new UpstreamDecisionClient(upstream);

        var call = client.EvaluateAsync(new MinosRequest(Definition, "s")).AsTask();
        client.Dispose();
        var thrown = new ObjectDisposedException("upstream");
        gate.SetException(thrown);
        var error = (await call).Error;

        Assert.Equal(DecisionErrorKind.Disposed, error.Kind);
        Assert.Equal("The client was disposed while the request was in flight.", error.Message);
        Assert.Same(thrown, error.Exception);
    }

    // ---- GetService ----

    [Fact]
    public void GetService_ReturnsTheMinosMetadata_FromTheUpstreamMetadata()
    {
        using var upstream = new FakeUpstreamClient { Metadata = new UpstreamMetadata("prov", new Uri("https://example.com/v1"), "model-d") };
        using var client = new UpstreamDecisionClient(upstream);

        var metadata = client.GetService<MinosMetadata>();

        Assert.Equal(new MinosMetadata("prov", new Uri("https://example.com/v1"), "model-d"), metadata);
        Assert.Same(upstream.Metadata, client.GetService<UpstreamMetadata>());
    }

    [Fact]
    public void GetService_NamesAFixedProvider_WhenTheUpstreamClientReportsNoNameOrNoMetadata()
    {
        using var named = new FakeUpstreamClient { Metadata = new UpstreamMetadata() };
        using var unnamed = new UpstreamDecisionClient(named);
        using var bare = new FakeUpstreamClient { Metadata = null };
        using var noMetadata = new UpstreamDecisionClient(bare);

        Assert.Equal(new MinosMetadata(UpstreamDecisionClient.DefaultProviderName, null, null), unnamed.GetService<MinosMetadata>());
        Assert.Equal(new MinosMetadata(UpstreamDecisionClient.DefaultProviderName, null, null), noMetadata.GetService<MinosMetadata>());
    }

    [Fact]
    public void GetService_ReturnsTheResolvedCapabilities_AsANewInstanceEachCall()
    {
        using var listed = new FakeUpstreamClient { Metadata = new UpstreamMetadata(supportedKinds: [DecisionKind.Binary, DecisionKind.Score]) };
        using var client = new UpstreamDecisionClient(listed);
        using var unlisted = new FakeUpstreamClient { Metadata = null };
        using var assumed = new UpstreamDecisionClient(unlisted);

        var capabilities = client.GetService<DecisionCapabilities>()!;
        Assert.Equal(QuestionKinds.Noul | QuestionKinds.Score, capabilities.QuestionKinds);
        Assert.Null(capabilities.MaxQuestions);
        Assert.Null(capabilities.MaxChoiceOptions);
        Assert.False(capabilities.SupportsImages);
        Assert.False(capabilities.SupportsModelListing);
        Assert.NotSame(capabilities, client.GetService<DecisionCapabilities>());

        Assert.Equal(QuestionKinds.All, assumed.GetService<DecisionCapabilities>()!.QuestionKinds);
    }

    [Fact]
    public void GetService_AnswersForItself_ThenAsksTheUpstreamClient()
    {
        using var upstream = new FakeUpstreamClient();
        using var client = new UpstreamDecisionClient(upstream);

        Assert.Same(client, client.GetService<UpstreamDecisionClient>());
        Assert.Same(client, client.GetService<Minos.IDecisionClient>());
        Assert.Same(upstream, client.GetService<UpstreamClient>());
        Assert.Same(upstream, client.GetService<FakeUpstreamClient>());

        // A key goes to the upstream client only.
        Assert.Null(client.GetService(typeof(MinosMetadata), "key"));
        Assert.Equal("key", upstream.LastServiceKey);
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new UpstreamDecisionClient(null!));
        using var upstream = new FakeUpstreamClient();
        using var client = new UpstreamDecisionClient(upstream);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.EvaluateAsync(null!));
    }

    /// <summary>An upstream client that records each request and returns or throws what the test sets.</summary>
    private sealed class FakeUpstreamClient : UpstreamClient
    {
        public List<(UpstreamRequest Request, DecisionOptions? Options, CancellationToken Token)> Requests { get; } = [];

        public Func<UpstreamRequest, DecisionOptions?, UpstreamResponse> Respond { get; set; } = (request, _) => Answer(request);

        public Func<UpstreamRequest, DecisionOptions?, Task<UpstreamResponse>>? RespondAsync { get; set; }

        public Exception? ThrowSynchronously { get; set; }

        public UpstreamMetadata? Metadata { get; set; } = new("fake");

        public object? LastServiceKey { get; private set; }

        public int DisposeCount { get; private set; }

        public static UpstreamResponse Answer(UpstreamRequest request, UsageDetails? usage = null, IReadOnlyDictionary<string, object?>? properties = null)
        {
            var answers = request.Questions.Select(DecisionAnswer (q) => q switch
            {
                BinaryDecisionQuestion => new BinaryDecisionAnswer(q.Id, 0.6),
                ChoiceDecisionQuestion c => new ChoiceDecisionAnswer(q.Id, c.Candidates[0].Id, [.. c.Candidates.Select((x, i) => new DecisionProbability(x.Id, i == 0 ? 1.0 : 0.0))]),
                ScoreDecisionQuestion s => new ScoreDecisionAnswer(q.Id, 0, [.. s.Levels.Select((x, i) => new DecisionProbability(x.Id, i == 0 ? 1.0 : 0.0))]),
                _ => throw new InvalidOperationException(),
            }).ToArray();
            return new UpstreamResponse(request, answers, usage: usage, additionalProperties: properties);
        }

        public Task<UpstreamResponse> GetResponseAsync(UpstreamRequest request, DecisionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add((request, options, cancellationToken));
            if (ThrowSynchronously is { } exception)
            {
                throw exception;
            }

            if (RespondAsync is { } respondAsync)
            {
                return respondAsync(request, options);
            }

            return RespondLater(request, options);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            LastServiceKey = serviceKey;
            if (serviceKey is not null)
            {
                return null;
            }

            if (serviceType == typeof(UpstreamMetadata))
            {
                return Metadata;
            }

            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose() => DisposeCount++;

        private async Task<UpstreamResponse> RespondLater(UpstreamRequest request, DecisionOptions? options)
        {
            await Task.Yield();
            return Respond(request, options);
        }
    }
}
