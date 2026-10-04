using System.Net;
using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// Native AOT smoke test: publishes with PublishAot and exercises the real client and generated code paths over a
/// canned handler. Exits 0 when every check passes, 1 otherwise.
/// </summary>
internal static class Program
{
    internal const string NoulResponse = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    internal const string ModelsResponse = """{"models":[{"name":"jev-latest","description":"The most recent stable, official release.","release_date":"2026-09-15"}]}""";
    internal const string ValidationResponse = """{"detail":"questions.is_urgent.instructions is required"}""";
    private const string TriageAnswers = """{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""";
    internal const string TriageResponse = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    private const string CredentialsResponse = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    private static int failures;

    private static async Task<int> Main()
    {
        await EvaluateParsesAnswers().ConfigureAwait(false);
        await ValidationErrorCarriesDetail().ConfigureAwait(false);
        await ListModelsReturnsModels().ConfigureAwait(false);
        await MalformedBodyIsInvalidResponse().ConfigureAwait(false);
        await OpenRouterModelListingIsUnsupported().ConfigureAwait(false);
        await OverloadedThenSuccessIsRetried().ConfigureAwait(false);
        GeneratedQuestionSetRoundTrips();
        StructuredQuestionSetRoundTrips();
        await TypedEvaluateAsyncParsesAnswers().ConfigureAwait(false);
        await TypedEvaluateAsyncWithTStateParsesAnswers().ConfigureAwait(false);
        await DefaultInterfaceMethodFallbackParsesAnswers().ConfigureAwait(false);
        await BuiltQuestionSetEvaluates().ConfigureAwait(false);
        await BuiltEnumChoiceReadsTheFieldsInDeclarationOrder().ConfigureAwait(false);
        await LoggingChecks.RetriedEvaluationLogsTheRetryAndTheSuccess().ConfigureAwait(false);
        await LoggingChecks.TypedEvaluationLogsItsQuestionCount().ConfigureAwait(false);
        await LoggingChecks.FailedEvaluationLogsTheLibraryMessageOnly().ConfigureAwait(false);
        await LoggingChecks.ModelListingLogsTheModelCount().ConfigureAwait(false);
        await DependencyInjectionChecks.DefaultAndKeyedClientsEvaluate().ConfigureAwait(false);
        await DependencyInjectionChecks.ClientsBoundFromConfigurationEvaluate().ConfigureAwait(false);
        DependencyInjectionChecks.InvalidConfigurationFailsValidation();
        await TelemetryChecks.RetriedEvaluationIsOneSpanOverTwoAttempts().ConfigureAwait(false);
        await TelemetryChecks.TypedEvaluationRecordsItsMetrics().ConfigureAwait(false);
        await TelemetryChecks.FailedEvaluationIsAnError().ConfigureAwait(false);
        await TelemetryChecks.CancelledEvaluationSetsErrorTypeWithoutItsMessage().ConfigureAwait(false);

        AllocationChecks.GeneratedParse();
        AllocationChecks.ReadNoul();
        AllocationChecks.ReadChoice();
        AllocationChecks.ReadScore();
        AllocationChecks.EvaluateRoundTrip();
        AllocationChecks.TypedEvaluateRoundTrip();
        AllocationChecks.EvaluateRoundTripWithNullLoggerFactory();
        AllocationChecks.TypedEvaluateRoundTripWithEveryLevelFiltered();
        AllocationChecks.EvaluateRoundTripWithDiscardingLogger();
        AllocationChecks.TypedEvaluateRoundTripWithDiscardingLogger();
        await AllocationChecks.DisabledLoggerAddsNothingWhereAnEnabledOneDoes().ConfigureAwait(false);
        AllocationChecks.ContentFromValue();
        AllocationChecks.ContentFromUtf8Json();
        AllocationChecks.BuildQuestionSet();
        AllocationChecks.EvaluateBuiltSetRoundTrip();
        AllocationChecks.JevAnswersGet();
        AllocationChecks.PatternHelpers();
        AllocationChecks.EvaluateRoundTripThroughDependencyInjection();
        AllocationChecks.EvaluateRoundTripThroughBoundConfiguration();
        await AllocationChecks.TelemetryOffAsynchronousTypedEvaluation().ConfigureAwait(false);
        AllocationChecks.EvaluateRoundTripWhileListening();
        AllocationChecks.TypedEvaluateRoundTripWhileListening();
        AllocationChecks.EvaluateBuiltSetRoundTripWhileListening();

        Console.WriteLine(failures == 0 ? "AOT smoke: all checks passed" : "AOT smoke: " + failures + " check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    private static async Task EvaluateParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, NoulResponse);
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer { Noul: 0.95 }, "EvaluateAsync parses a Noul answer");
    }

    private static async Task ValidationErrorCarriesDetail()
    {
        using var http = Http(HttpStatusCode.UnprocessableEntity, ValidationResponse);
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(
            result.IsFailure
                && result.Error.Kind == JevErrorKind.Validation
                && result.Error.Detail is { } detail
                && string.Equals(detail.GetProperty("detail").GetString(), "questions.is_urgent.instructions is required", StringComparison.Ordinal),
            "a 422 maps to Validation with its JSON detail");
    }

    private static async Task ListModelsReturnsModels()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new JevClient(http, Options());

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(
            result.IsSuccess && result.Value.Models.Count == 1 && string.Equals(result.Value.Models[0].Name, "jev-latest", StringComparison.Ordinal),
            "ListModelsAsync parses the models");
    }

    private static async Task MalformedBodyIsInvalidResponse()
    {
        using var http = Http(HttpStatusCode.OK, "not json");
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == JevErrorKind.InvalidResponse, "a malformed 2xx body maps to InvalidResponse");
    }

    private static async Task OpenRouterModelListingIsUnsupported()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new JevClient(http, Options(JevProvider.OpenRouter));

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == JevErrorKind.Unsupported, "model listing on OpenRouter is Unsupported");
    }

    private static async Task OverloadedThenSuccessIsRetried()
    {
        var handler = new SequenceHandler(NoulResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key", InitialBackoff = TimeSpan.FromMilliseconds(10) });

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsSuccess && handler.Calls == 2, "a 503 is retried through the resilience proxy");
    }

    private static void GeneratedQuestionSetRoundTrips()
    {
        using var questions = JsonDocument.Parse(SmokeTriage.QuestionsUtf8.ToArray());
        Check(
            string.Equals(
                questions.RootElement.GetProperty("team").GetProperty("criteria").GetProperty("account").GetString(),
                "Login, profile, permissions",
                StringComparison.Ordinal),
            "QuestionsUtf8 carries the Choice criteria");

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(TriageAnswers));
        reader.Read();
        var triage = SmokeTriage.Parse(ref reader);
        Check(
            !triage.RequestsCredentials.Value && triage.Team.Value == Team.Account && triage.Urgency.Value == Urgency.High,
            "the generated Parse reads typed answers");
    }

    private static void StructuredQuestionSetRoundTrips()
    {
        using var generated = JsonDocument.Parse(SmokeStructured.QuestionsUtf8.ToArray());
        Check(
            string.Equals(
                generated.RootElement.GetProperty("team").GetProperty("criteria").GetProperty("billing").GetProperty("not_for")[0].GetString(),
                "How much is Pro?",
                StringComparison.Ordinal),
            "Examples and NotFor are sent as a criterion object");

        using var built = JsonDocument.Parse(SmokeBuiltSet.Structured().QuestionsUtf8.ToArray());
        var root = built.RootElement;
        Check(
            root.GetProperty("requests_credentials").GetProperty("instructions").GetProperty("policy").GetProperty("strict").GetBoolean(),
            "JevContent instructions are sent as a JSON object");
        Check(
            string.Equals(
                root.GetProperty("team").GetProperty("criteria").GetProperty("account").GetProperty("owner").GetString(),
                "identity",
                StringComparison.Ordinal),
            "a JevCriterion.Json description is sent as JSON");
    }

    private static async Task TypedEvaluateAsyncParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, TriageResponse);
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync<SmokeTriage>("Help! My payouts have been failing for 3 days.").ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.RequestsCredentials.Value
                && result.Value.Team.Value == Team.Account
                && result.Value.Urgency.Value == Urgency.High,
            "EvaluateAsync<T>(string) parses typed answers over the raw, pooled-buffer path");
    }

    private static async Task TypedEvaluateAsyncWithTStateParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, CredentialsResponse);
        using var client = new JevClient(http, Options());
        var state = new SmokeState("Payouts failing", "Help! My payouts have been failing for 3 days.");

        var result = await client.EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState).ConfigureAwait(false);

        Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value,
            "EvaluateAsync<T, TState>(state, stateTypeInfo) parses typed answers over the raw, pooled-buffer path");
    }

    private static async Task DefaultInterfaceMethodFallbackParsesAnswers()
    {
        // DimFallbackClient implements only IJevClient's two abstract members, so this call runs the interface's
        // default implementation, not JevClient's raw, pooled-buffer override, proving the compatible, allocating
        // fallback path also compiles and runs under Native AOT.
        IJevClient client = new DimFallbackClient(new SystemOneResponse
        {
            Model = "jev-1.13.0",
            Answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
            {
                ["requests_credentials"] = new NoulAnswer { Noul = 0.1 },
                ["team"] = new ChoiceAnswer
                {
                    Choice = "account",
                    Probabilities = new Dictionary<string, double>(StringComparer.Ordinal) { ["billing"] = 0.2, ["account"] = 0.8 },
                    Confidence = 0.7,
                },
                ["urgency"] = new ScoreAnswer
                {
                    Score = 1.9,
                    Legend = new Dictionary<string, string>(StringComparer.Ordinal) { ["0"] = "Can wait", ["1"] = "This week", ["2"] = "Today" },
                    Probabilities = new Dictionary<string, double>(StringComparer.Ordinal) { ["0"] = 0.0, ["1"] = 0.1, ["2"] = 0.9 },
                    Confidence = 0.8,
                },
            },
            Usage = new JevUsage { InputTokens = 296, OutputTokens = 20 },
        });

        var result = await client.EvaluateAsync<SmokeTriage>("Help! My payouts have been failing for 3 days.").ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.RequestsCredentials.Value
                && result.Value.Team.Value == Team.Account
                && result.Value.Urgency.Value == Urgency.High,
            "the default interface method fallback parses typed answers under Native AOT");
    }

    private static async Task BuiltQuestionSetEvaluates()
    {
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        Check(set.Warnings.Count == 0, "a question set built at run time passes its rules");

        using var http = Http(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson);
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(set, "Help! My payouts have been failing for 3 days.").ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.Get(credentials).Value
                && result.Value.Get(team).Value == Team.Account
                && string.Equals(result.Value.Get(product).Value, "pro-plan", StringComparison.Ordinal)
                && result.Value.Get(urgency).Value == Urgency.High,
            "a built question set evaluates over the raw, pooled-buffer path");

        var invalid = JevQuestionSet.CreateBuilder().Choice("empty", "Which one?", out _).Build();
        Check(
            invalid.IsFailure
                && invalid.Error.Kind == JevErrorKind.InvalidQuestions
                && string.Equals(invalid.Error.Failures[0].Rule, "JEV001", StringComparison.Ordinal),
            "a built question set that breaks a rule fails with its JEV id");
    }

    private static async Task BuiltEnumChoiceReadsTheFieldsInDeclarationOrder()
    {
        var set = SmokeBuiltSet.AliasedChoice(out var channel);
        Check(
            string.Equals(Encoding.UTF8.GetString(set.QuestionsUtf8), SmokeBuiltSet.AliasedChoiceQuestions, StringComparison.Ordinal),
            "a built enum Choice sends its options in declaration order, the alias skipped, under Native AOT");

        using var http = Http(HttpStatusCode.OK, SmokeBuiltSet.AliasedChoiceResponseJson);
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(set, "Sent from my phone's mail app.").ConfigureAwait(false);

        Check(
            result.IsSuccess
                && result.Value.Get(channel).Value == Channel.Email
                && Math.Abs(result.Value.Get(channel).Probabilities[Channel.Mail] - 0.8) < 1e-12,
            "a built enum Choice keys an aliased value by its first declared name, email, under Native AOT");
    }

    private static HttpClient Http(HttpStatusCode status, string body)
        => new(new CannedHandler(status, body)) { BaseAddress = new Uri("https://example.test/api/") };

    private static JevClientOptions Options(JevProvider provider = JevProvider.TypeSafe)
        => new() { ApiKey = "smoke-key", Provider = provider };

    internal static SystemOneRequest Request() => new()
    {
        State = "Help! My payouts have been failing for 3 days.",
        Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
        },
    };

    internal static void Check(bool passed, string description)
    {
        Console.WriteLine((passed ? "PASS " : "FAIL ") + description);
        if (!passed)
        {
            failures++;
        }
    }
}
