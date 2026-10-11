using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Minos.Tests;
using ZeroAlloc.Results;
using static Minos.OpenAI.Tests.OpenAIClientTestKit;

namespace Minos.OpenAI.Tests;

public sealed class OpenAIDecisionClientTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        foreach (ref var http in CollectionsMarshal.AsSpan(_httpClients))
        {
            http.Dispose();
        }
    }

    [Fact]
    public async Task TypedCall_PostsToV1Decisions_WithTheBearerKeyAndTheProtocolBody_AndReturnsTheTypedAnswers()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Success()));
        var pool = new CountingPool();
        using var client = new OpenAIDecisionClient(
            Settings(o => o.Model = null), httpClient: null, handler, TimeProvider.System, pool, logger: null);

        var result = await client.EvaluateAsync<TicketCheck>("I was charged twice.");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var sent = OnlyRequest(handler);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("https://api.openai.com/v1/decisions"), sent.Uri);
        Assert.Equal("Bearer k", sent.Authorization);
        Assert.Matches("^Minos\\.NET/[^ +]+$", sent.UserAgent);
        var expected = ExpectedBody(new DecisionRequest(TestSets.AllKinds(), DecisionContent.FromString("I was charged twice.")), "gpt-6-luna");
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(sent.Body!)), sent.Body);

        var answers = result.Value;
        Assert.Equal(0.92, answers.Refund.Probability);
        Assert.Equal(TicketTopic.Billing, answers.Topic.Value);
        Assert.Equal(0.6, answers.Topic.Confidence);
        Assert.Equal(TicketUrgency.Soon, answers.Urgency.Value);
        Assert.Equal(1.1, answers.Urgency.Expected, 10);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void Metadata_NamesOpenAI_TheEndpointAndTheModel()
    {
        using var client = Client(_httpClients, new StubHandler((_, _) => Task.FromResult(Success())));
        using var defaults = Owned(new StubHandler((_, _) => Task.FromResult(Success())));

        var metadata = client.GetService<DecisionClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("openai", metadata.ProviderName);
        Assert.Equal(new Uri("https://api.openai.com/"), metadata.Endpoint);
        Assert.Equal(TestModel, metadata.DefaultModel);
        Assert.Equal(new Uri("https://api.openai.com/"), defaults.GetService<DecisionClientMetadata>()!.Endpoint);
    }

    [Fact]
    public void Capabilities_HaveImages_AndEveryPropertySet()
    {
        using var client = Client(_httpClients, new StubHandler((_, _) => Task.FromResult(Success())));

        var capabilities = client.GetService<DecisionCapabilities>();

        Assert.NotNull(capabilities);
        Assert.True(capabilities.Images);
        Assert.Equal(QuestionKinds.All, capabilities.QuestionKinds);
        Assert.False(capabilities.ModelListing);
        Assert.NotSame(capabilities, client.GetService<DecisionCapabilities>());
    }

    [Fact]
    public void RetryOptions_AreAClone_OfTheResolvedSettings()
    {
        using var client = Client(_httpClients, new StubHandler((_, _) => Task.FromResult(Success())), configure: o => o.MaxRetries = 4);

        var first = client.GetService<DecisionRetryOptions>();
        var second = client.GetService<DecisionRetryOptions>();

        Assert.NotNull(first);
        Assert.Equal(4, first.MaxRetries);
        Assert.NotSame(first, second);
        first.MaxRetries = 0;
        Assert.Equal(4, client.GetService<DecisionRetryOptions>()!.MaxRetries);
    }

    [Fact]
    public void GetService_FindsTheClientAndItsPipelineStages()
    {
        using var client = Client(_httpClients, new StubHandler((_, _) => Task.FromResult(Success())));

        Assert.Same(client, client.GetService<OpenAIDecisionClient>());
        Assert.NotNull(client.GetService<RetryingDecisionClient>());
        Assert.NotNull(client.GetService<OpenTelemetryDecisionClient>());
        Assert.Null(client.GetService<LoggingDecisionClient>());
    }

    [Fact]
    public async Task A503_IsRetriedUpToMaxRetries()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}")));
        using var client = Client(_httpClients, handler, configure: o => o.MaxRetries = 2);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ARefusal_IsNotRetried()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, FixtureText("refusal.json"))));
        using var client = Client(_httpClients, handler, configure: o => o.MaxRetries = 2);

        var result = await client.EvaluateAsync(new DecisionRequest(TestSets.WithOtherQuestion(), DecisionContent.FromString("x")));

        Assert.Equal(DecisionErrorKind.Refused, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        _ = OnlyRequest(handler);
    }

    [Fact]
    public async Task ANoulWithWhenTrue_AndImages_WithImagesOff_IsUnsupported_Min206ThenMin204_AndSendsNothing()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Success()));
        using var client = Client(_httpClients, handler, configure: o => o.Capabilities = new DecisionCapabilities { Images = false });
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("refund", "Is this a refund?", whenTrue: "Money back"));
        var request = new DecisionRequest(definition, DecisionContent.FromString("x"))
        {
            Images = [DecisionImage.FromBytes(new byte[] { 0x89, 0x50 }, "image/png")],
        };

        var result = await client.EvaluateAsync(request);

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(["MIN206", "MIN204"], result.Error.Failures.Select(failure => failure.Rule));
        Assert.Equal("OpenAI does not support images.", result.Error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task WithoutTheStandardPipeline_OneAttemptIsSent()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}")));
        using var client = Client(_httpClients, handler, configure: o =>
        {
            o.MaxRetries = 2;
            o.UseStandardPipeline = false;
        });

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
        _ = OnlyRequest(handler);
        Assert.Null(client.GetService<RetryingDecisionClient>());
        Assert.Null(client.GetService<OpenTelemetryDecisionClient>());
    }

    [Fact]
    public async Task ACallThroughALogger_LogsEvent1001_WithProviderOpenAI()
    {
        using var logs = new LogCapture();
        using var client = Client(_httpClients, new StubHandler((_, _) => Task.FromResult(Success())), loggerFactory: logs.Factory);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        var record = logs.Only(1001);
        Assert.Equal("openai", LogAssert.Field(record, "Provider"));
        Assert.Equal(TestModel, LogAssert.Field(record, "Model"));
        Assert.NotNull(client.GetService<LoggingDecisionClient>());
    }

    [Fact]
    public async Task OwnedClient_TornDownInFlight_IsDisposed_AfterOneAttempt_AndDisposesItsHandler()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Success();
        });
        var pool = new CountingPool();
        var client = Owned(handler, pool: pool);

        var call = client.EvaluateAsync(Request()).AsTask();
        await entered.Task;
        client.Dispose();
        var result = await call;

        Assert.Equal(DecisionErrorKind.Disposed, result.Error.Kind);
        Assert.Equal("The client was disposed while the request was in flight.", result.Error.Message);
        _ = OnlyRequest(handler);
        Assert.True(handler.Disposed);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task BorrowedAttempt_ThatSucceedsAfterDispose_StillSucceeds_AndTheHttpClientIsNotDisposed()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Success();
        });
        var client = Client(_httpClients, handler, configure: o => o.MaxRetries = 2);

        var call = client.EvaluateAsync(Request()).AsTask();
        await entered.Task;
        client.Dispose();
        release.SetResult();
        var result = await call;

        Assert.True(result.IsSuccess);
        Assert.False(handler.Disposed);
    }

    [Fact]
    public async Task EveryCallAfterDispose_Throws_AndDisposeTwiceDoesNothing()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Success()));
        var client = Owned(handler);
        client.Dispose();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(Request()));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync<TicketCheck>("x"));
        Assert.Empty(handler.Requests);
        Assert.True(handler.Disposed);
    }

    [Fact]
    public void BorrowedHttpClient_WithoutABaseAddress_GetsTheResolvedOne()
    {
        var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(Success())));
        _httpClients.Add(http);

        using var client = new OpenAIDecisionClient(http, new OpenAIDecisionClientOptions { ApiKey = "k" });

        Assert.Equal(new Uri("https://api.openai.com/"), http.BaseAddress);
    }

    [Fact]
    public void BorrowedHttpClient_WithABaseAddressWithoutATrailingSlash_Throws()
    {
        var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(Success()))) { BaseAddress = new Uri("http://proxy.local/api") };
        _httpClients.Add(http);

        var exception = Assert.Throws<ArgumentException>(() => new OpenAIDecisionClient(http, new OpenAIDecisionClientOptions { ApiKey = "k" }));

        Assert.StartsWith("httpClient.BaseAddress must end with '/'.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullHttpClient_Throws_BeforeTheOptionsAreRead()
        => Assert.Throws<ArgumentNullException>(() => new OpenAIDecisionClient(null!, new OpenAIDecisionClientOptions()));

    [Fact]
    public void ConfigureHttpClient_SetsTheBaseAddressTimeoutAndUserAgentOnce_WithoutAKey()
    {
        var http = new HttpClient();
        _httpClients.Add(http);
        var options = new OpenAIDecisionClientOptions { Timeout = TimeSpan.FromSeconds(7) };

        OpenAIDecisionClient.ConfigureHttpClient(http, options, name => string.Equals(name, "OPENAI_BASE_URL", StringComparison.Ordinal) ? "http://env.local/v1" : null);
        OpenAIDecisionClient.ConfigureHttpClient(http, options, _ => null);

        Assert.Equal(new Uri("http://env.local/v1/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), http.Timeout);
        Assert.Matches("^Minos\\.NET/[^ +]+$", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    private static DecisionRequest Request() => new(TestSets.AllKinds(), DecisionContent.FromString("I was charged twice."));
}
