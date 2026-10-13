using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;
using Minos.SystemOne;
using Minos.SystemOne.Protocols;
using Minos.SystemOne.Serialization;
using Minos.SystemOne.Transport;
using Minos.Transport;
using Minos.Transport.Rest;
using ZeroAlloc.Rest.SystemTextJson;

namespace Minos.Tests;

public sealed class DecisionTransportTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    [Fact]
    public void Metadata_endpoint_is_the_base_address_plus_the_path()
    {
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"));
        using var custom = ClientTestKit.Client(
            _httpClients,
            StubHandler.Json(HttpStatusCode.OK, "{}"),
            provider: SystemOneProvider.Custom,
            configure: options =>
            {
                options.BaseAddress = new Uri("http://localhost:8080/router/");
                options.Path = "decide/v1/systemone";
            });

        var defaultEndpoint = client.GetService<DecisionClientMetadata>()!.Endpoint;
        Assert.Equal(new Uri(SystemOneDefaults.TypeSafeBaseAddress, "v1/systemone"), defaultEndpoint);
        Assert.Equal(new Uri("http://localhost:8080/router/decide/v1/systemone"), custom.GetService<DecisionClientMetadata>()!.Endpoint);
    }

    [Fact]
    public async Task SendAsync_keeps_the_slash_in_a_runtime_path()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/base/") };
        _httpClients.Add(http);
        var pool = new CountingPool();
        var api = new DecisionApiClient(
            http,
            new SystemTextJsonSerializer(DecisionJsonContext.Default),
            new DecisionRawSerializer(pool),
            new DecisionErrorMapper(TimeProvider.System, disposed: null, SystemOneProtocol.Instance));
        using var body = RawJson.Create(pool, 16);
        "{}"u8.CopyTo(body.GetSpan(2));
        body.Advance(2);

        var result = await api.SendAsync(body, "v1/systemone", "Bearer k", retryCount: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        result.Value.Dispose();
        Assert.Equal("/base/v1/systemone", ClientTestKit.OnlyRequest(handler).Uri!.AbsolutePath);
    }

    [Fact]
    public async Task Posts_the_protocol_request_to_its_endpoint()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, handler, pool);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.Answers[0].Value);
        Assert.Equal("jev-1.13.0", result.Value.Model);
        var sent = ClientTestKit.OnlyRequest(handler);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/v1/systemone", sent.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("Bearer test-key", sent.Authorization);
        Assert.Equal(
            """{"state":"a ticket","model":"jev-test-model","questions":""" + Encoding.UTF8.GetString(SystemOneProtocol.QuestionsJson(QuestionSets.UrgencyDefinition())) + "}",
            sent.Body);
        Assert.Null(sent.RetryCount);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Sends_the_retry_count_on_a_retry_attempt()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler);

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s") { RetryAttempt = 2 });

        Assert.Equal("2", ClientTestKit.OnlyRequest(handler).RetryCount);
    }

    [Fact]
    public async Task A_request_model_overrides_the_configured_one()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler);

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s") { Model = "minos-other" });

        Assert.Contains("\"model\":\"minos-other\"", ClientTestKit.OnlyRequest(handler).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_an_error_status_through_the_protocol()
    {
        var handler = StubHandler.Json(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""");
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, handler, pool);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.RateLimited, result.Error.Kind);
        Assert.Equal(429, result.Error.StatusCode);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task An_unreadable_response_returns_every_buffer()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"answers":{}}""");
        var pool = new CountingPool();
        using var client = ClientTestKit.Client(_httpClients, handler, pool);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Does_not_retry_on_its_own()
    {
        var handler = StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        var settings = SystemOneClientSettings.Resolve(
            new SystemOneClientOptions { ApiKey = "test-key", Model = ClientTestKit.TestModel, MaxRetries = 3, InitialBackoff = TimeSpan.FromMilliseconds(1), UseStandardPipeline = false },
            _ => null);
        using var client = new SystemOneClient(settings, http, ownedHandler: null, TimeProvider.System);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
        ClientTestKit.OnlyRequest(handler);
    }

    [Fact]
    public void Metadata_names_the_provider_endpoint_and_model()
    {
        using var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"), provider: SystemOneProvider.OpenRouter);

        var metadata = client.GetService<DecisionClientMetadata>();

        Assert.Equal("openrouter", metadata!.ProviderName);
        Assert.Equal(ClientTestKit.TestModel, metadata.DefaultModel);
        Assert.NotNull(metadata.Endpoint);
        Assert.Same(client, client.GetService<SystemOneClient>());
        Assert.Same(client, client.GetService<IDecisionClient>());
        Assert.Null(client.GetService<string>());
        Assert.Null(client.GetService<DecisionClientMetadata>(serviceKey: "key"));
    }

    [Fact]
    public void A_disposed_client_throws_at_entry()
    {
        var client = ClientTestKit.Client(_httpClients, StubHandler.Json(HttpStatusCode.OK, "{}"));
        client.Dispose();

        ClientTestKit.ThrowsSynchronously<ObjectDisposedException>(
            () => client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s")).AsTask());
    }

    [Fact]
    public async Task An_attempt_after_the_client_was_disposed_is_Disposed_without_being_sent()
    {
        var calls = 0;
        RawSend send = (_, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("The transport sent an attempt after disposal.");
        };
        var pool = new CountingPool();
        var settings = SystemOneClientSettings.Resolve(new SystemOneClientOptions { ApiKey = "k" }, static _ => null);
        using var transport = new DecisionTransport(
            send,
            SystemOneProtocol.Instance,
            settings.Capabilities,
            settings.Provider.ToString(),
            settings.Model,
            new DecisionClientMetadata("typesafe", null, null), pool, static () => true);

        var call = transport.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.True(call.IsCompletedSuccessfully);
        var result = await call;
        Assert.Equal(DecisionErrorKind.Disposed, result.Error.Kind);
        Assert.Equal(0, calls);
        Assert.Equal(0, pool.Rented);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Capability_failures_come_before_the_protocols_in_one_Unsupported_error_and_nothing_is_sent()
    {
        var calls = 0;
        var pool = new CountingPool();
        // Three questions against a limit of two, and no Score: MIN202, then MIN201 for severity.
        var capabilities = new CapabilitySet(QuestionKinds.Noul | QuestionKinds.Choice, MaxQuestions: 2, MaxChoiceOptions: null, Images: false, ModelListing: false);
        using var transport = Transport(capabilities, new FailingProtocol("is_urgent", "department"), pool, () => calls++);

        var result = await transport.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(
            new (string, string?)[] { ("MIN202", null), ("MIN201", "severity"), ("TEST1", "is_urgent"), ("TEST1", "department") },
            result.Error.Failures.Select(f => (f.Rule, f.QuestionKey)).ToArray());
        Assert.Equal(result.Error.Failures[0].Message, result.Error.Message);
        Assert.Equal(0, calls);
        Assert.Equal(0, pool.Rented);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task A_protocol_failure_alone_is_Unsupported_and_nothing_is_sent()
    {
        var calls = 0;
        var pool = new CountingPool();
        var capabilities = new CapabilitySet(QuestionKinds.All, MaxQuestions: null, MaxChoiceOptions: null, Images: true, ModelListing: true);
        using var transport = Transport(capabilities, new FailingProtocol("department"), pool, () => calls++);

        var result = await transport.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), "s"));

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal([new QuestionFailure("TEST1", "department", "test does not support department.")], result.Error.Failures);
        Assert.Equal("test does not support department.", result.Error.Message);
        Assert.Equal(0, calls);
        Assert.Equal(0, pool.Rented);
        Assert.Equal(0, pool.Outstanding);
    }

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    private static DecisionTransport Transport(CapabilitySet capabilities, IDecisionProtocol protocol, CountingPool pool, Action onSend)
    {
        RawSend send = (_, _, _) =>
        {
            onSend();
            throw new InvalidOperationException("The transport sent an unsupported request.");
        };
        return new DecisionTransport(
            send, protocol, capabilities, "test", "m", new DecisionClientMetadata("test", null, null), pool, static () => false);
    }

    // Fails each named question, in question order; anything else it is asked to do is a test failure.
    private sealed class FailingProtocol(params string[] keys) : IDecisionProtocol
    {
        public RawJson WriteRequest<TArg>(
            QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState,
            IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
            where TArg : allows ref struct
            => throw new InvalidOperationException("The transport wrote an unsupported request.");

        public void CollectUnsupported(QuestionSetDefinition definition, string providerName, ref List<QuestionFailure>? failures)
        {
            foreach (var question in definition.QuestionArray)
            {
                if (keys.Contains(question.Key))
                {
                    (failures ??= []).Add(new QuestionFailure("TEST1", question.Key, providerName + " does not support " + question.Key + "."));
                }
            }
        }

        public TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create)
            => throw new NotSupportedException();

        public ZeroAlloc.Results.Result<DecisionResponse, DecisionError> ReadResponse(ReadOnlySpan<byte> body, QuestionSetDefinition definition)
            => throw new NotSupportedException();

        public DecisionError MapError(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter, string? requestId)
            => throw new NotSupportedException();
    }
}
