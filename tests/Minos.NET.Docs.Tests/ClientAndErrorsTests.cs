using System.Globalization;
using System.Net;
using System.Text.Json;
using Minos.SystemOne;

namespace Minos.Docs.Tests;

// Serialized with the other test class that sets the key environment variables.
[Collection("key environment")]
public sealed class ClientAndErrorsTests
{
    private const string UrgencyResponse = """
        {
          "model": "jev-1.13.0",
          "answers": { "is_urgent": { "type": "noul", "noul": 0.93 } },
          "usage": { "input_tokens": 41, "output_tokens": 3 }
        }
        """;

    private const string ModelsResponse = """
        {
          "models": [
            { "name": "jev-latest", "description": "The most recent stable release.", "release_date": "2026-09-15" },
            { "name": "jev-preview", "description": "The most recent release.", "release_date": "2026-09-16" }
          ]
        }
        """;

    // Bodies TypeSafe really sent, captured by the Live smoke workflow's run 37913354273 on 2026-10-09.
    private const string TypeSafeValidationBody = """{"detail":[{"type":"too_short","loc":["body","questions"],"msg":"Dictionary should have at least 1 item after validation, not 0","input":{},"ctx":{"field_type":"Dictionary","min_length":1,"actual_length":0}}]}""";

    private const string TypeSafeUnauthorizedBody = """{"detail":{"error_type":"authentication_error","message":"Cannot authenticate with the server. Please check your API key and try again."}}""";

    [Fact]
    public async Task Describe_TurnsARejectedKeyIntoAnAction()
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(), Reply.Error(401));
        using (http)
        using (client)
        {
            var message = await RawRequests.UrgencyAsync(client, "Help!", CancellationToken.None);

            Assert.Equal("The service rejected the API key. Check the key and what it may access.", message);
        }

        Assert.Collection(requests, _ => { });
    }

    [Theory]
    [InlineData(422, "{\"detail\":\"questions is required\"}", "The service rejected the request: {\"detail\":\"questions is required\"}")]
    [InlineData(400, "not json", "The service rejected the request: The API returned HTTP 400.")]
    [InlineData(418, "", "The service failed with HTTP 418: The API returned HTTP 418.")]
    public async Task Describe_ReadsTheStatusAndTheDetail(int status, string body, string expected)
    {
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Error(status, body));
        using (http)
        using (client)
        {
            Assert.Equal(expected, await RawRequests.UrgencyAsync(client, "Help!", CancellationToken.None));
        }
    }

    [Fact]
    public async Task ValidationProblems_ReadsWhereAndWhatFromTypeSafesRealBody()
    {
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Error(422, TypeSafeValidationBody));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(new SystemOneRequest
            {
                State = "Help!",
                Questions = new Dictionary<string, Question>(),
            });

            Assert.True(result.IsFailure);
            Assert.Equal(
                ["body.questions: Dictionary should have at least 1 item after validation, not 0"],
                ValidationProblems.List(result.Error));
        }
    }

    [Fact]
    public async Task ValidationProblems_SkipsMalformedProblems()
    {
        const string body = """{"detail":[{"loc":["body","state"],"msg":"too long"},{"msg":"x"},3,{"loc":"body","msg":"y"},{"loc":["body"],"msg":7}]}""";
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Error(422, body));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(new SystemOneRequest
            {
                State = "Help!",
                Questions = new Dictionary<string, Question>(),
            });

            Assert.True(result.IsFailure);
            Assert.Equal(["body.state: too long"], ValidationProblems.List(result.Error));
        }
    }

    [Theory]
    [InlineData(401, TypeSafeUnauthorizedBody)]
    [InlineData(422, TypeSafeUnauthorizedBody)]
    [InlineData(422, "{\"detail\":\"questions is required\"}")]
    [InlineData(422, "not json")]
    public async Task ValidationProblems_IsEmpty_WhenThereIsNoListOfProblems(int status, string body)
    {
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Error(status, body));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(new SystemOneRequest
            {
                State = "Help!",
                Questions = new Dictionary<string, Question>(),
            });

            Assert.True(result.IsFailure);
            Assert.Empty(ValidationProblems.List(result.Error));
        }
    }

    [Theory]
    [InlineData(500, "The service failed with HTTP 500: The API returned HTTP 500.")]
    [InlineData(503, "The service is busy. Try again later.")]
    [InlineData(429, "The service is busy. Try again later.")]
    public async Task Describe_ReadsAServerFailureAfterTheRetriesAreUsedUp(int status, string expected)
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(1), Reply.Error(status));
        using (http)
        using (client)
        {
            Assert.Equal(expected, await RawRequests.UrgencyAsync(client, "Help!", CancellationToken.None));
        }

        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task Describe_ReportsTheWaitTheServerAskedFor()
    {
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Error(429, string.Empty, ("retry-after-ms", "1500")));
        using (http)
        using (client)
        {
            Assert.Equal(
                "The service is busy. It asks for 1500 ms before the next call.",
                await RawRequests.UrgencyAsync(client, "Help!", CancellationToken.None));
        }
    }

    [Fact]
    public async Task Describe_ReportsANetworkFailureAndAnUnreadableReply()
    {
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Refused);
        using (http)
        using (client)
        {
            Assert.Equal(
                "The service could not be reached: The connection was refused.",
                await RawRequests.UrgencyAsync(client, "Help!", CancellationToken.None));
        }

        var (http2, client2, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Ok("this is not json"));
        using (http2)
        using (client2)
        {
            var result = await client2.ListModelsAsync(CancellationToken.None);

            Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
            Assert.Equal(200, result.Error.StatusCode);
            Assert.StartsWith("The service replied with something unreadable", ClientFailures.Describe(result.Error), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheRawRequest_SendsTheQuestionsAndReadsTheAnswers()
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(), Reply.Ok(UrgencyResponse));
        using (http)
        using (client)
        {
            var message = await RawRequests.UrgencyAsync(client, "Help!", CancellationToken.None);

            Assert.Equal("jev-1.13.0: 93 %, 41 input tokens", message);
        }

        Assert.Collection(requests, _ => { });
        using var body = JsonDocument.Parse(requests[0].Body);
        Assert.Equal("jev-latest", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("noul", body.RootElement.GetProperty("questions").GetProperty("is_urgent").GetProperty("type").GetString());
        Assert.Equal("Bearer docs-key", requests[0].Authorization);
        Assert.Null(requests[0].RetryCount);
    }

    [Fact]
    public async Task ModelsAsync_ListsTheModels_AndOpenRouterIsUnsupportedWithoutARequest()
    {
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(), Reply.Ok(ModelsResponse));
        using (http)
        using (client)
        {
            Assert.Equal("jev-latest (2026-09-15), jev-preview (2026-09-16)", await ModelListing.ModelsAsync(client, CancellationToken.None));
        }

        var options = new SystemOneClientOptions { Provider = SystemOneProvider.OpenRouter, ApiKey = "docs-key" };
        using var handler = new ScriptedDecision.Handler([Reply.Ok(ModelsResponse)]);
        using var openRouterHttp = new HttpClient(handler);
        using var openRouter = new SystemOneClient(openRouterHttp, options);

        var result = await openRouter.ListModelsAsync(CancellationToken.None);

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Empty(handler.Requests);
        Assert.StartsWith(
            "The provider cannot do that",
            await ModelListing.ModelsAsync(openRouter, CancellationToken.None),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheOptionsOnThePage_AreTheDefaults()
    {
        var spelledOut = ClientOptions.SpelledOut("docs-key");
        var defaults = new SystemOneClientOptions();

        Assert.Equal(defaults.Provider, spelledOut.Provider);
        Assert.Equal(defaults.Model, spelledOut.Model);
        Assert.Equal(defaults.Timeout, spelledOut.Timeout);
        Assert.Equal(defaults.MaxRetries, spelledOut.MaxRetries);
        Assert.Equal(defaults.InitialBackoff, spelledOut.InitialBackoff);
        Assert.Equal(defaults.MaxRetryDelay, spelledOut.MaxRetryDelay);
        Assert.Equal(defaults.Jitter, spelledOut.Jitter);
        Assert.Equal(defaults.UseStandardPipeline, spelledOut.UseStandardPipeline);
        Assert.Null(defaults.ApiKey);
        Assert.Null(defaults.BaseAddress);
        Assert.Null(defaults.Model);
        Assert.Equal("jev-latest", SystemOneDefaults.Model);
    }

    [Fact]
    public void Validate_ThrowsTheConstructorsExceptions()
    {
        Assert.Null(ClientOptions.Problem(ClientOptions.SpelledOut("docs-key")));
        Assert.Null(ClientOptions.Problem(ClientOptions.Patient("docs-key")));
        Assert.Null(ClientOptions.Problem(ClientOptions.NoRetries("docs-key")));
        Assert.Null(ClientOptions.Problem(ClientOptions.Strict("docs-key")));
        Assert.Equal(
            "MaxRetries must be between 0 and 10. (Parameter 'options')",
            ClientOptions.Problem(new SystemOneClientOptions { ApiKey = "docs-key", MaxRetries = 11 }));
        Assert.Null(ClientOptions.Problem(new SystemOneClientOptions { ApiKey = "docs-key", MaxRetries = 10 }));
        Assert.StartsWith(
            "MaxRetryDelay must be at least InitialBackoff",
            ClientOptions.Problem(new SystemOneClientOptions
            {
                ApiKey = "docs-key",
                InitialBackoff = TimeSpan.FromSeconds(2),
                MaxRetryDelay = TimeSpan.FromSeconds(1),
            }),
            StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new SystemOneClient(new SystemOneClientOptions { ApiKey = "docs-key", MaxRetries = 11 }));
    }

    [Fact]
    public void Validate_NeedsAnApiKey_FromTheOptionsOrTheEnvironment()
    {
        using var environment = new KeyEnvironment(typeSafe: null, openRouter: null);

        var typeSafe = ClientOptions.Problem(new SystemOneClientOptions());
        var openRouter = ClientOptions.Problem(new SystemOneClientOptions { Provider = SystemOneProvider.OpenRouter });

        Assert.Contains("TYPESAFE_API_KEY", typeSafe, StringComparison.Ordinal);
        Assert.Contains("OPENROUTER_API_KEY", openRouter, StringComparison.Ordinal);

        KeyEnvironment.Set(typeSafe: "from-the-environment", openRouter: null);

        Assert.Null(ClientOptions.Problem(new SystemOneClientOptions()));
        Assert.Contains(
            "OPENROUTER_API_KEY",
            ClientOptions.Problem(new SystemOneClientOptions { Provider = SystemOneProvider.OpenRouter }),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryableFailure_IsRetriedAndTheAttemptNumberIsSent()
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(), Reply.Error(503), Reply.Error(500), Reply.Ok(UrgencyResponse));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        Assert.Equal([null, "1", "2"], requests.Select(r => r.RetryCount));
    }

    [Theory]
    [InlineData(429, DecisionErrorKind.RateLimited)]
    [InlineData(503, DecisionErrorKind.Overloaded)]
    [InlineData(529, DecisionErrorKind.Overloaded)]
    [InlineData(500, DecisionErrorKind.Server)]
    [InlineData(408, DecisionErrorKind.Http)]
    public async Task EachRetriedStatus_IsTriedMaxRetriesPlusOneTimes_ThenReportedWithItsKind(int status, DecisionErrorKind kind)
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(), Reply.Error(status));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);

            Assert.Equal(kind, result.Error.Kind);
            Assert.Equal(status, result.Error.StatusCode);
        }

        Assert.Equal(3, requests.Count);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task AClientError_IsNotRetried(int status)
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(), Reply.Error(status));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);

            Assert.True(result.IsFailure);
        }

        Assert.Collection(requests, _ => { });
    }

    [Fact]
    public async Task ANetworkFailure_IsRetried()
    {
        var (http, client, requests) = ScriptedDecision.Client(ScriptedDecision.Quick(1), Reply.Refused);
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);

            Assert.Equal(DecisionErrorKind.Network, result.Error.Kind);
            Assert.IsType<HttpRequestException>(result.Error.Exception);
        }

        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task MaxRetriesZero_SendsOneRequest()
    {
        var options = ClientOptions.NoRetries("docs-key");
        var (http, client, requests) = ScriptedDecision.Client(options, Reply.Error(503));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);

            Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
        }

        Assert.Collection(requests, _ => { });
    }

    [Fact]
    public async Task TheRetryAfterHeaders_AreReadAndTheMillisecondFormWins()
    {
        await AssertRetryAfterAsync(TimeSpan.FromSeconds(3), ("Retry-After", "3"));
        await AssertRetryAfterAsync(TimeSpan.FromMilliseconds(1500), ("retry-after-ms", "1500"));
        await AssertRetryAfterAsync(TimeSpan.FromMilliseconds(250), ("Retry-After", "7"), ("retry-after-ms", "250"));
        await AssertRetryAfterAsync(TimeSpan.FromSeconds(7), ("Retry-After", "7"), ("retry-after-ms", "soon"));
        await AssertRetryAfterAsync(TimeSpan.FromSeconds(7), ("retry-after-ms", "-5"), ("Retry-After", "7"));
        await AssertRetryAfterAsync(null, ("Retry-After", "soon"));
        await AssertRetryAfterAsync(null);

        var date = DateTimeOffset.UtcNow.AddMinutes(10).ToString("R", CultureInfo.InvariantCulture);
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Error(429, string.Empty, ("Retry-After", date)));
        using (http)
        using (client)
        {
            var wait = (await client.EvaluateAsync(Request(), CancellationToken.None)).Error.RetryAfter;

            Assert.InRange(wait!.Value, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
        }
    }

    [Fact]
    public async Task ARetryAfterLongerThanMaxRetryDelay_IsCappedAtIt()
    {
        // The server asks for an hour. MaxRetryDelay is 5 ms, so the retry happens at once.
        var (http, client, requests) = ScriptedDecision.Client(
            ScriptedDecision.Quick(1), Reply.Error(429, string.Empty, ("Retry-After", "3600")), Reply.Ok(UrgencyResponse));
        using (http)
        using (client)
        using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            var result = await client.EvaluateAsync(Request(), limit.Token);

            Assert.True(result.IsSuccess);
        }

        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task ARetryAfterBelowMaxRetryDelay_IsWaitedFor()
    {
        // The backoff alone would be 1 ms, so a wait of about 400 ms can only come from the server's header.
        var options = ScriptedDecision.Quick(1);
        options.MaxRetryDelay = TimeSpan.FromSeconds(5);
        var (http, client, requests) = ScriptedDecision.Client(
            options, Reply.Error(429, string.Empty, ("retry-after-ms", "400")), Reply.Ok(UrgencyResponse));
        using (http)
        using (client)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);
            var waited = System.Diagnostics.Stopwatch.GetElapsedTime(started);

            Assert.True(result.IsSuccess);
            Assert.True(waited >= TimeSpan.FromMilliseconds(350), $"Waited {waited}.");
        }

        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task AnAttemptThatTakesTooLong_IsATimeout_AndIsRetried()
    {
        var options = ScriptedDecision.Quick(1);
        options.Timeout = TimeSpan.FromMilliseconds(100);
        var (http, client, requests) = ScriptedDecision.Client(
            options, new Reply(HttpStatusCode.OK, UrgencyResponse, Delay: TimeSpan.FromSeconds(30)));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);

            Assert.Equal(DecisionErrorKind.Timeout, result.Error.Kind);
            Assert.NotNull(result.Error.Exception);
        }

        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task Cancelling_ThrowsInsteadOfReturningAFailure()
    {
        var (http, client, _) = ScriptedDecision.Client(
            ScriptedDecision.Quick(), new Reply(HttpStatusCode.OK, UrgencyResponse, Delay: TimeSpan.FromSeconds(30)));
        using (http)
        using (client)
        using (var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(Request(), cancelled.Token));
        }
    }

    [Fact]
    public async Task ABorrowedHttpClient_IsNotDisposedWithTheClient()
    {
        using var handler = new ScriptedDecision.Handler([Reply.Ok(UrgencyResponse)]);
        using var borrowed = new HttpClient(handler) { BaseAddress = new Uri("https://docs.example/api/") };
        var client = ClientConstructors.Borrowed(borrowed, "docs-key");
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(Request(), CancellationToken.None));
        using var response = await borrowed.GetAsync(new Uri("v1/anything", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void ABorrowedHttpClient_KeepsItsOwnTimeout_UnlessConfiguredThroughConfigureHttpClient()
    {
        var options = new SystemOneClientOptions { ApiKey = "docs-key", Timeout = TimeSpan.FromSeconds(5) };
        using var borrowed = new HttpClient();
        using var client = new SystemOneClient(borrowed, options);

        Assert.Equal(TimeSpan.FromSeconds(100), borrowed.Timeout);

        using var configured = new HttpClient();
        SystemOneClient.ConfigureHttpClient(configured, options);

        Assert.Equal(TimeSpan.FromSeconds(5), configured.Timeout);
    }

    private static async Task AssertRetryAfterAsync(TimeSpan? expected, params (string Name, string Value)[] headers)
    {
        var (http, client, _) = ScriptedDecision.Client(ScriptedDecision.Quick(0), Reply.Error(429, string.Empty, headers));
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(Request(), CancellationToken.None);

            Assert.Equal(expected, result.Error.RetryAfter);
        }
    }

    // The SystemOneDefaults table lists every public member of the class, with the values the class holds.
    [Fact]
    public void TheDecisionDefaultsTable_ListsEveryMemberWithItsValue()
    {
        var rows = PageTables.Rows("client-and-errors.md", "SystemOneDefaults");
        var actual = typeof(SystemOneDefaults)
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is System.Reflection.MemberTypes.Field or System.Reflection.MemberTypes.Property)
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(actual, rows.Select(row => PageTables.Code(row[0])).Order(StringComparer.Ordinal));
        var values = rows.ToDictionary(row => PageTables.Code(row[0]), row => PageTables.Code(row[1]), StringComparer.Ordinal);
        Assert.Equal(SystemOneDefaults.ApiKeyEnvironmentVariable, values[nameof(SystemOneDefaults.ApiKeyEnvironmentVariable)]);
        Assert.Equal(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, values[nameof(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable)]);
        Assert.Equal(SystemOneDefaults.BaseAddressEnvironmentVariable, values[nameof(SystemOneDefaults.BaseAddressEnvironmentVariable)]);
        Assert.Equal(SystemOneDefaults.Model, values[nameof(SystemOneDefaults.Model)]);
        Assert.Equal(SystemOneDefaults.TypeSafeBaseAddress.ToString(), values[nameof(SystemOneDefaults.TypeSafeBaseAddress)]);
        Assert.Equal(SystemOneDefaults.OpenRouterBaseAddress.ToString(), values[nameof(SystemOneDefaults.OpenRouterBaseAddress)]);
    }

    // The kinds table lists every member of the enum, with the same name.
    [Fact]
    public void TheKindsTable_ListsEveryDecisionErrorKind()
    {
        var rows = PageTables.Rows("client-and-errors.md", "The kinds");

        Assert.Equal(
            Enum.GetNames<DecisionErrorKind>().Order(StringComparer.Ordinal),
            rows.Select(row => PageTables.Code(row[0])).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheCustomPreset_SendsToTheAddressAndPath_AndNeverReadsAKeyFromTheEnvironment()
    {
        using var environment = new Variables(
            (SystemOneDefaults.ApiKeyEnvironmentVariable, "typesafe-key"),
            (SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, "openrouter-key"));
        var (client, handler, http) = Direct(ProviderOptions.Custom(), Reply.Ok(UrgencyResponse));
        using (http)
        using (handler)
        using (client)
        {
            _ = await client.EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        var options = ProviderOptions.Custom();
        options.Provider = SystemOneProvider.VllmSemanticRouter;
        options.ApiKey = "my-key";
        var (second, secondHandler, secondHttp) = Direct(options, Reply.Ok(UrgencyResponse));
        using (secondHttp)
        using (secondHandler)
        using (second)
        {
            _ = await second.EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        Assert.Collection(handler.Requests, _ => { });
        var sent = handler.Requests[0];
        Assert.Equal("http://localhost:8080/api/v1/systemone", sent.Uri!.AbsoluteUri);
        Assert.Null(sent.Authorization);
        Assert.Collection(secondHandler.Requests, _ => { });
        Assert.Equal("Bearer my-key", secondHandler.Requests[0].Authorization);
    }

    [Fact]
    public void ThePresetsTable_MatchesWhatEachPresetDoes()
    {
        using var environment = new Variables(
            (SystemOneDefaults.ApiKeyEnvironmentVariable, null),
            (SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, null),
            (SystemOneDefaults.BaseAddressEnvironmentVariable, null));
        var rows = PageTables.Rows("client-and-errors.md", "The presets");

        Assert.Equal(
            Enum.GetNames<SystemOneProvider>().Order(StringComparer.Ordinal),
            rows.Select(row => PageTables.Code(row[0])).Order(StringComparer.Ordinal));
        foreach (var row in rows)
        {
            var provider = Enum.Parse<SystemOneProvider>(PageTables.Code(row[0]));
            var hosted = provider is SystemOneProvider.TypeSafe or SystemOneProvider.OpenRouter;
            var keyVariable = hosted ? PageTables.Code(row[3]) : null;
            var modelCodes = PageTables.AllCode(row[4]);
            var model = modelCodes.Length > 0 ? modelCodes[0] : null;
            var options = new SystemOneClientOptions
            {
                Provider = provider,
                BaseAddress = hosted ? null : new Uri("http://localhost:8080/"),
                Model = model is null ? "my-model" : null,
            };

            // Without a key a hosted preset fails and names the variable on the page; the others need none.
            if (keyVariable is null)
            {
                Assert.Null(ClientOptions.Problem(options));
            }
            else
            {
                Assert.Contains(keyVariable, ClientOptions.Problem(options), StringComparison.Ordinal);
                options.ApiKey = "key";
                Assert.Null(ClientOptions.Problem(options));
            }

            // The default model is the one on the page, or there is none and the model must be set.
            options.Model = null;
            Assert.Equal(model is null, ClientOptions.Problem(options)?.Contains("no default model", StringComparison.Ordinal) ?? false);
            if (model is not null)
            {
                Assert.Equal(SystemOneDefaults.Model, model);
            }

            // The base address and the path.
            options.Model = model ?? "my-model";
            using var http = new HttpClient();
            SystemOneClient.ConfigureHttpClient(http, options);
            var expected = hosted
                ? PageTables.Code(row[1])
                : "http://localhost:8080/";
            Assert.Equal(expected, http.BaseAddress!.AbsoluteUri);
            Assert.Equal("v1/systemone", PageTables.AllCode(row[2])[0]);
        }

        Assert.Equal(SystemOneDefaults.TypeSafeBaseAddress.AbsoluteUri, PageTables.Code(rows[0][1]));
        Assert.Equal(SystemOneDefaults.OpenRouterBaseAddress.AbsoluteUri, PageTables.Code(rows[1][1]));
        Assert.Equal(SystemOneDefaults.ApiKeyEnvironmentVariable, PageTables.Code(rows[0][3]));
        Assert.Equal(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, PageTables.Code(rows[1][3]));
    }

    [Fact]
    public void TheOptionsTable_ListsEveryPropertyOfTheOptions()
    {
        var rows = PageTables.Rows("client-and-errors.md", "Options");
        var properties = typeof(SystemOneClientOptions).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(properties.Order(StringComparer.Ordinal), rows.Select(row => PageTables.Code(row[0])).Order(StringComparer.Ordinal));
        Assert.Equal(12, properties.Length);
        Assert.Contains("has twelve properties", PageTables.Text("client-and-errors.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCapabilityDefaultsTable_MatchesWhatEachPresetResolves()
    {
        var rows = PageTables.Rows("client-and-errors.md", "The defaults");

        Assert.Equal(
            Enum.GetNames<SystemOneProvider>().Order(StringComparer.Ordinal),
            rows.Select(row => PageTables.Code(row[0])).Order(StringComparer.Ordinal));
        foreach (var row in rows)
        {
            var provider = Enum.Parse<SystemOneProvider>(PageTables.Code(row[0]));
            var options = new SystemOneClientOptions
            {
                Provider = provider,
                ApiKey = "key",
                BaseAddress = new Uri("http://localhost:8080/"),
                Model = "my-model",
            };
            using var http = new HttpClient();
            using var client = new SystemOneClient(http, options);

            var capabilities = Assert.IsType<DecisionCapabilities>(client.GetService(typeof(DecisionCapabilities)));

            Assert.Equal(QuestionKinds.All, capabilities.QuestionKinds);
            Assert.Equal("every kind", row[1]);
            Assert.Equal(Limit(row[2]), capabilities.MaxQuestions);
            Assert.Equal(Limit(row[3]), capabilities.MaxChoiceOptions);
            Assert.Equal(Yes(row[4]), capabilities.Images);
            Assert.Equal(Yes(row[5]), capabilities.ModelListing);
        }
    }

    [Fact]
    public async Task ANoulOnlyServer_RefusesAChoiceQuestionWithoutSendingIt()
    {
        var (client, handler, http) = Direct(CapabilityOptions.NoulOnly(), Reply.Ok(UrgencyResponse));
        using (http)
        using (handler)
        using (client)
        {
            var refused = await client.EvaluateAsync<TicketCheck>("Help!", CancellationToken.None);

            Assert.Equal(DecisionErrorKind.Unsupported, refused.Error.Kind);
            Assert.Equal("Custom does not support Choice questions.", refused.Error.Message);
            Assert.Equal(["MIN201 team: Custom does not support Choice questions."], CapabilityOptions.Problems(refused.Error));
            Assert.Empty(handler.Requests);
            Assert.StartsWith("The provider cannot do that", ClientFailures.Describe(refused.Error), StringComparison.Ordinal);

            _ = await client.EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);
        }

        Assert.Collection(handler.Requests, _ => { });
        Assert.Empty(CapabilityOptions.Problems(new DecisionError(DecisionErrorKind.Network, "down")));
    }

    [Fact]
    public async Task TheLimits_AreMin202ForTheQuestionsAndMin203ForTheChoiceOptions()
    {
        var options = CapabilityOptions.NoulOnly();
        options.Capabilities = new DecisionCapabilities { MaxQuestions = 1, MaxChoiceOptions = 2 };
        var (client, handler, http) = Direct(options, Reply.Ok(UrgencyResponse));
        using (http)
        using (handler)
        using (client)
        {
            var result = await client.EvaluateAsync<TicketCheck>("Help!", CancellationToken.None);

            Assert.Equal(
                [
                    "MIN202 : Custom accepts at most 1 question per request; the set has 2.",
                    "MIN203 team: Custom accepts at most 2 options per Choice question; 'team' has 3.",
                ],
                CapabilityOptions.Problems(result.Error));
        }

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void TheResolvedCapabilities_AreThePresetsWithTheOverridesApplied()
    {
        using var http = new HttpClient();
        using var client = new SystemOneClient(http, new SystemOneClientOptions { Provider = SystemOneProvider.OpenRouter, ApiKey = "key" });
        using var noul = new SystemOneClient(http, CapabilityOptions.NoulOnly());
        using var own = new AlwaysUrgent();

        Assert.Equal("All, no question limit, model listing off", CapabilityOptions.Describe(client));
        Assert.Equal("Noul, no question limit, model listing off", CapabilityOptions.Describe(noul));
        Assert.Equal("This client has no System One capabilities.", CapabilityOptions.Describe(own));
        Assert.NotSame(client.GetService<DecisionCapabilities>(), client.GetService<DecisionCapabilities>());
    }

    [Fact]
    public async Task ModelListing_FollowsTheCapability_AndAnUnsupportedListSendsNothing()
    {
        var options = ProviderOptions.Custom();
        options.Capabilities = new DecisionCapabilities { ModelListing = true };
        var (client, handler, http) = Direct(options, Reply.Ok(ModelsResponse));
        using (http)
        using (handler)
        using (client)
        {
            Assert.Equal("jev-latest (2026-09-15), jev-preview (2026-09-16)", await ModelListing.ModelsAsync(client, CancellationToken.None));
        }

        Assert.Collection(handler.Requests, sent => Assert.Equal("http://localhost:8080/v1/models", sent.Uri!.AbsoluteUri));

        // Custom's default is off: a self-hosted server's model list need not use TypeSafe's shape.
        var (refused, refusedHandler, refusedHttp) = Direct(ProviderOptions.Custom(), Reply.Ok(ModelsResponse));
        using (refusedHttp)
        using (refusedHandler)
        using (refused)
        {
            var result = await refused.ListModelsAsync(CancellationToken.None);

            Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
            Assert.Equal("Model listing is not available on Custom.", result.Error.Message);
        }

        Assert.Empty(refusedHandler.Requests);
    }

    // A cell of the capability table: "none" for no limit, or a number.
    private static int? Limit(string cell)
        => string.Equals(cell, "none", StringComparison.Ordinal) ? null : int.Parse(cell, CultureInfo.InvariantCulture);

    // A cell of the capability table: "yes" or "no".
    private static bool Yes(string cell)
    {
        Assert.True(cell is "yes" or "no", $"{cell} is yes or no.");
        return string.Equals(cell, "yes", StringComparison.Ordinal);
    }

    // A client over a handler, with no BaseAddress of its own, so that the options decide where requests go.
    private static (SystemOneClient Client, ScriptedDecision.Handler Handler, HttpClient Http) Direct(
        SystemOneClientOptions options, params Reply[] script)
    {
        var handler = new ScriptedDecision.Handler(script);
        var http = new HttpClient(handler, disposeHandler: false);
        options.MaxRetries = 0;
        SystemOneClient.ConfigureHttpClient(http, options);
        return (new SystemOneClient(http, options), handler, http);
    }

    // Sets environment variables for a test and puts them back afterwards.
    private sealed class Variables : IDisposable
    {
        private readonly (string Name, string? Value)[] _before;

        public Variables(params (string Name, string? Value)[] variables)
        {
            _before = [.. variables.Select(variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name)))];
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _before)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static SystemOneRequest Request() => new()
    {
        State = "Help!",
        Questions = new Dictionary<string, Question> { ["is_urgent"] = new NoulQuestion { Instructions = "Urgent?" } },
    };
}
