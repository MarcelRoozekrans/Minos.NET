using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Jev.Serialization;

namespace ZeroAlloc.Jev.Tests;

/// <summary>What <see cref="JevClient"/> logs through an <see cref="ILoggerFactory"/>.</summary>
public sealed class JevClientLoggingTests : IDisposable
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
    public async Task RetriedAttempt_LogsAttemptRetrying_WithItsNumber()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Sequence(() => Status(503), Success), logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        var retrying = logs.Only(1003);
        Assert.Equal(LogLevel.Warning, retrying.Level);
        Assert.Equal("1", LogAssert.Field(retrying, "Attempt"));
        Assert.Equal("Overloaded", LogAssert.Field(retrying, "ErrorKind"));
        Assert.Equal("503", LogAssert.Field(retrying, "StatusCode"));
        Assert.Null(LogAssert.Field(retrying, "RetryAfter"));
    }

    [Fact]
    public async Task ExhaustedRetries_LogOnePerRetry_AndNoneForTheLastAttempt()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(() => Status(503));
        using var client = Client(handler, logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsFailure);
        Assert.Equal(3, handler.Requests.Count);
        var attempts = logs.Records.Where(record => record.Id.Id == 1003).Select(record => LogAssert.Field(record, "Attempt")!).ToArray();
        Assert.Equal(["1", "2"], attempts);
    }

    [Fact]
    public async Task RetryAfter_IsLogged()
    {
        using var logs = new LogCapture();
        var rateLimited = () =>
        {
            var response = Status(429);
            response.Headers.TryAddWithoutValidation("retry-after-ms", "20");
            return response;
        };
        using var client = Client(StubHandler.Sequence(rateLimited, Success), logs.Factory, maxRetries: 1);

        await client.EvaluateAsync(Request());

        var retrying = logs.Only(1003);
        Assert.Equal("RateLimited", LogAssert.Field(retrying, "ErrorKind"));
        Assert.Equal("00:00:00.0200000", LogAssert.Field(retrying, "RetryAfter"));
    }

    [Fact]
    public async Task EveryPath_LogsItsRetries()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(
            () => Status(503),
            Success,
            () => Status(503),
            Success,
            () => Status(503),
            () => Json(HttpStatusCode.OK, Fixture.Text("models.json")));
        using var client = Client(handler, logs.Factory, maxRetries: 1);

        Assert.True((await client.EvaluateAsync<UrgencyCheck>("Help!")).IsSuccess);
        Assert.True((await client.EvaluateAsync(OneNoulSet(), "Help!")).IsSuccess);
        Assert.True((await client.ListModelsAsync()).IsSuccess);

        Assert.Equal(3, logs.Records.Count(record => record.Id.Id == 1003));
    }

    [Fact]
    public async Task EveryLevelDisabled_LogsNothing()
    {
        using var logs = new LogCapture(LogLevel.None);
        using var client = Client(StubHandler.Sequence(() => Status(503), () => Status(422)), logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(JevErrorKind.Validation, result.Error.Kind);
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task NonTransientFailure_LogsNoRetry_AndSendsOneRequest()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(() => Status(422));
        using var client = Client(handler, logs.Factory, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(JevErrorKind.Validation, result.Error.Kind);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single, not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Single(handler.Requests);
#pragma warning restore HLQ005
        Assert.DoesNotContain(1003, logs.EventIds);
    }

    [Fact]
    public async Task TransientFailure_WithNoRetriesAllowed_LogsNoRetry()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(() => Status(503));
        using var client = Client(handler, logs.Factory, maxRetries: 0);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsFailure);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single, not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Single(handler.Requests);
#pragma warning restore HLQ005
        Assert.DoesNotContain(1003, logs.EventIds);
    }

    [Fact]
    public async Task NullFactory_RetriesAsBefore()
    {
        var handler = StubHandler.Sequence(() => Status(503), Success);
        using var client = Client(handler, loggerFactory: null, maxRetries: 2);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Logger_UsesTheJevClientCategory()
    {
        using var logs = new LogCapture();
        using var client = Client(StubHandler.Sequence(() => Status(503), Success), logs.Factory, maxRetries: 1);

        await client.EvaluateAsync(Request());

        Assert.Equal("ZeroAlloc.Jev.JevClient", logs.Only(1003).Category);
        Assert.Equal(JevLog.Category, logs.Only(1003).Category);
    }

    private JevClient Client(StubHandler handler, ILoggerFactory? loggerFactory, int maxRetries = 0, JevProvider provider = JevProvider.TypeSafe)
    {
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        return new JevClient(
            http,
            new JevClientOptions
            {
                ApiKey = "test-key",
                Provider = provider,
                Model = ClientTestKit.TestModel,
                MaxRetries = maxRetries,
                InitialBackoff = TimeSpan.FromMilliseconds(1),
                Jitter = false,
            },
            loggerFactory);
    }

    private static JevQuestionSet OneNoulSet()
        => JevQuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out _).Build().Value;

    private static HttpResponseMessage Success() => Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));

    private static HttpResponseMessage Status(int status) => Json((HttpStatusCode)status, "{}");

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SystemOneRequest Request()
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), JevJsonContext.Default.SystemOneRequest)!;
}
