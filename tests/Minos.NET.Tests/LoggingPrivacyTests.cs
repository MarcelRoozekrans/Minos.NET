using System.Net;
using System.Text;

namespace Minos.Tests;

/// <summary>
/// Sends distinctive text everywhere a caller or the server puts content: the state, a question key, the instructions,
/// the criteria, the API key, an answer, an error body and a malformed body. None of it may reach a log record's
/// message, structured values or exception. Each test first proves the text really travelled, in the captured request
/// or in <see cref="JevError.Detail"/>, so a pass is not vacuous. The status kinds the mapper produces, Http,
/// Validation, RateLimited, Server, Overloaded and Unauthorized, and Network, Timeout and InvalidResponse, are all covered.
/// </summary>
public sealed class LoggingPrivacyTests : IDisposable
{
    private const string Secret = "zq-secret";
    private const string ApiKey = "zq-secret-api-key";

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task RawSuccess_LogsNoRequestOrAnswerText()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Json(
            HttpStatusCode.OK,
            """{"model":"m","answers":{"zq-secret-key":{"type":"choice","choice":"zq-secret-answer","probabilities":{"zq-secret-answer":1.0},"confidence":0.9}},"usage":{"input_tokens":1,"output_tokens":1}}""");
        using var client = Client(handler, logs);

        var result = await client.EvaluateAsync(SecretRequest());

        Assert.True(result.IsSuccess);
        AssertSentTheSecrets(handler);
        Assert.Equal("zq-secret-answer", Assert.IsType<ChoiceAnswer>(result.Value.Answers["zq-secret-key"]).Choice);
        AssertNoSecret(logs);
    }

    // Every status kind the mapper produces, each with a secret in the JSON error body that becomes JevError.Detail.
    [Theory]
    [InlineData(404, JevErrorKind.Http)]
    [InlineData(422, JevErrorKind.Validation)]
    [InlineData(429, JevErrorKind.RateLimited)]
    [InlineData(500, JevErrorKind.Server)]
    public async Task StatusError_LogsNoDetail(int status, JevErrorKind kind)
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Json((HttpStatusCode)status, """{"detail":"zq-secret-detail"}""");
        using var client = Client(handler, logs);

        var result = await client.EvaluateAsync(SecretRequest());

        Assert.Equal(kind, result.Error.Kind);
        AssertSentTheSecrets(handler);
        Assert.Contains(Secret, result.Error.Detail!.Value.GetRawText(), StringComparison.Ordinal);
        AssertNoSecret(logs);
    }

    [Fact]
    public async Task RetriedOverload_LogsNoErrorBody()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Json(HttpStatusCode.ServiceUnavailable, """{"detail":"zq-secret-overloaded"}""");
        using var client = Client(handler, logs);

        var result = await client.EvaluateAsync(SecretRequest());

        Assert.Equal(JevErrorKind.Overloaded, result.Error.Kind);
        AssertSentTheSecrets(handler);
        Assert.Contains(Secret, result.Error.Detail!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Equal([1003, 1002], logs.EventIds);
        AssertNoSecret(logs);
    }

    [Fact]
    public async Task MalformedSuccessBody_LogsNoBodyText()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Json(HttpStatusCode.OK, "zq-secret-body is not json");
        using var client = Client(handler, logs);

        var result = await client.EvaluateAsync(SecretRequest());

        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        AssertSentTheSecrets(handler);

        // The JSON reader's message quotes only the first rejected character, not the body, so the body cannot be
        // asserted in the error; a 200 InvalidResponse proves the malformed body was received and parsed.
        Assert.Equal(200, result.Error.StatusCode);
        AssertNoSecret(logs);
    }

    [Fact]
    public async Task TypedAnswerWithAnUnknownOption_LogsNotTheAnswer_ThoughTheErrorQuotesIt()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Json(
            HttpStatusCode.OK,
            """{"model":"m","answers":{"department":{"type":"choice","choice":"zq-secret-answer","probabilities":{"billing":1.0},"confidence":0.9}},"usage":{"input_tokens":1,"output_tokens":1}}""");
        using var client = Client(handler, logs);

        var result = await client.EvaluateAsync<DepartmentRouting>("zq-secret-state");

        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Contains(Secret, result.Error.Message, StringComparison.Ordinal);
        AssertNoSecret(logs);
    }

    [Fact]
    public async Task TypedAndBuiltSetSuccess_LogNoStateKeyOrInstructions()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Sequence(
            () => Json(HttpStatusCode.OK, Fixture.Text("response-noul.json")),
            () => Json(
                HttpStatusCode.OK,
                """{"model":"m","answers":{"zq-secret-key":{"type":"noul","noul":0.4}},"usage":{"input_tokens":1,"output_tokens":1}}"""));
        using var client = Client(handler, logs);
        var set = JevQuestionSet.CreateBuilder()
            .Noul("zq-secret-key", "zq-secret-instructions", out _, c => c.WhenTrue("zq-secret-true").WhenFalse("zq-secret-false"))
            .Build()
            .Value;

        Assert.True((await client.EvaluateAsync<UrgencyCheck>("zq-secret-state")).IsSuccess);
        Assert.True((await client.EvaluateAsync(set, "zq-secret-state")).IsSuccess);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Contains("zq-secret-state", request.Body, StringComparison.Ordinal));
        Assert.All(handler.Requests, request => Assert.Contains(ApiKey, request.Authorization, StringComparison.Ordinal));
        Assert.Contains("zq-secret-key", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("zq-secret-instructions", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("zq-secret-true", handler.Requests[1].Body, StringComparison.Ordinal);
        AssertNoSecret(logs);
    }

    [Fact]
    public async Task ListModelsUnauthorized_LogsNoBodyOrKey()
    {
        using var logs = new LogCapture();
        var handler = StubHandler.Json(HttpStatusCode.Unauthorized, """{"detail":"zq-secret-unauthorized"}""");
        using var client = Client(handler, logs);

        var result = await client.ListModelsAsync();

        Assert.Equal(JevErrorKind.Unauthorized, result.Error.Kind);
        Assert.Contains(ApiKey, handler.Requests[0].Authorization, StringComparison.Ordinal);
        Assert.Contains(Secret, result.Error.Detail!.Value.GetRawText(), StringComparison.Ordinal);
        AssertNoSecret(logs);
    }

    [Fact]
    public async Task NetworkFailure_LogsNoRequestText()
    {
        using var logs = new LogCapture();
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused to https://host/?q=zq-secret-exception"));
        using var client = Client(handler, logs);

        var result = await client.EvaluateAsync(SecretRequest());

        Assert.Equal(JevErrorKind.Network, result.Error.Kind);
        AssertSentTheSecrets(handler);

        // A real handler can echo request data into its exception message; the precondition is that it reached the error.
        Assert.Contains(Secret, result.Error.Message, StringComparison.Ordinal);
        AssertNoSecret(logs);
    }

    [Fact]
    public async Task Timeout_LogsNoRequestText()
    {
        using var logs = new LogCapture();
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var client = Client(handler, logs, timeout: TimeSpan.FromMilliseconds(100));

        var result = await client.EvaluateAsync(SecretRequest());

        Assert.Equal(JevErrorKind.Timeout, result.Error.Kind);
        AssertSentTheSecrets(handler);
        AssertNoSecret(logs);
    }

    private JevClient Client(StubHandler handler, LogCapture logs, TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler);
        if (timeout is { } perAttempt)
        {
            http.Timeout = perAttempt;
        }

        _httpClients.Add(http);
        return new JevClient(
            http,
            new JevClientOptions
            {
                ApiKey = ApiKey,
                Model = "jev-latest",
                MaxRetries = 1,
                InitialBackoff = TimeSpan.FromMilliseconds(1),
                Jitter = false,
            },
            logs.Factory);
    }

    private static SystemOneRequest SecretRequest() => new()
    {
        State = "zq-secret-state: my card number is 4111 1111 1111 1111",
        Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["zq-secret-key"] = new NoulQuestion
            {
                Instructions = "zq-secret-instructions",
                Criteria = new NoulCriteria { WhenTrue = "zq-secret-true", WhenFalse = "zq-secret-false" },
            },
        },
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // The request carried every secret, so the logs had the chance to leak them.
    private static void AssertSentTheSecrets(StubHandler handler)
    {
        var request = handler.Requests[0];
        Assert.Contains(ApiKey, request.Authorization, StringComparison.Ordinal);
        Assert.Contains("zq-secret-state", request.Body, StringComparison.Ordinal);
        Assert.Contains("zq-secret-key", request.Body, StringComparison.Ordinal);
        Assert.Contains("zq-secret-instructions", request.Body, StringComparison.Ordinal);
        Assert.Contains("zq-secret-true", request.Body, StringComparison.Ordinal);
    }

    private static void AssertNoSecret(LogCapture logs)
    {
        var records = logs.Records;
        Assert.NotEmpty(records);
        for (var r = 0; r < records.Count; r++)
        {
            var record = records[r];
            Assert.DoesNotContain(Secret, record.Message, StringComparison.Ordinal);
            var state = record.StructuredState ?? [];
            for (var i = 0; i < state.Count; i++)
            {
                Assert.DoesNotContain(Secret, state[i].Value ?? string.Empty, StringComparison.Ordinal);
            }

            Assert.Null(record.Exception);
        }
    }
}
