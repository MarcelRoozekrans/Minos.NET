using System.Net;
using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>
/// Native AOT smoke test: publishes with PublishAot and exercises the real client and generated code paths over a
/// canned handler. Exits 0 when every check passes, 1 otherwise.
/// </summary>
internal static class Program
{
    internal const string NoulResponse = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    internal const string ModelsResponse = """{"models":[{"name":"jev-latest","description":"The most recent stable, official release.","release_date":"2026-09-15"}]}""";
    internal const string ValidationResponse = """{"detail":"questions.is_urgent.instructions is required"}""";
    internal const string TriageAnswers = """{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""";
    internal const string TriageResponse = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    internal const string CredentialsResponse = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1}},"usage":{"input_tokens":296,"output_tokens":20}}""";

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
        await DependencyInjectionChecks.ClientsConfiguredFromTheEnvironmentEvaluate().ConfigureAwait(false);

        // One group per public type, so together with the checks above every public entry point runs under Native AOT.
        await DecisionClientChecks.ClientsThatOwnTheirHttpClientRefuseCallsAfterDispose().ConfigureAwait(false);
        await DecisionClientChecks.ClientOverAnHttpClientReadsTheKeyFromTheEnvironment().ConfigureAwait(false);
        await DecisionClientChecks.BuiltSetEvaluatesWithACancellationToken().ConfigureAwait(false);
        await DecisionClientChecks.TypedTextStateEvaluatesWithACancellationToken().ConfigureAwait(false);
        await DecisionClientChecks.TypedJsonStateEvaluates().ConfigureAwait(false);
        await DecisionClientChecks.TypedUtf8StateEvaluates().ConfigureAwait(false);
        await DecisionClientChecks.TypedStateEvaluatesWithACancellationToken().ConfigureAwait(false);
        await IDecisionClientChecks.AbstractMembersRunThroughTheInterface().ConfigureAwait(false);
        await IDecisionClientChecks.RequestDefaultMethodPassesTheRequestOn().ConfigureAwait(false);
        await IDecisionClientChecks.TypedDefaultMethodsSendTheStateAndParseAnswers().ConfigureAwait(false);
        await IDecisionClientChecks.TypedStateDefaultMethodsSendTheStateAndParseAnswers().ConfigureAwait(false);
        await IDecisionClientChecks.BuiltSetDefaultMethodsSendTheStateAndReadAnswers().ConfigureAwait(false);
        DecisionClientOptionsChecks.ValidateAcceptsValidOptionsAndRejectsInvalidOnes();
        DecisionContentChecks.TextContentRoundTrips();
        DecisionContentChecks.JsonContentRoundTrips();
        CriterionChecks.NotForTextsAreSentWithTheDescription();
        QuestionSetBuilderChecks.NoulCriteriaAndUndescribedKeyedOptionsAreSent();
        await QuestionHandleChecks.DefaultHandlesAreRejected().ConfigureAwait(false);
        DefinitionChecks.QuestionSetDefinitionKeepsItsQuestions();
        DefinitionChecks.EmptyAnswerSlotsHoldNoAnswers();
        DefinitionChecks.AnswerSlotsRefuseAnIndexOutOfRange();
        NoulChecks.EmptyNoulIsFalse();
        ChoiceChecks.ChoiceIsRebuiltAndCompared();
        ScoreChecks.ScoreIsRebuiltAndCompared();
        ProbabilityMapChecks.ProbabilityMapEnumeratesAndCompares();
        await KeyedChoiceChecks.KeyedChoiceIsRebuiltAndCompared().ConfigureAwait(false);
        await KeyedScoreChecks.KeyedScoreIsRebuiltAndCompared().ConfigureAwait(false);
        await KeyedProbabilityMapChecks.KeyedProbabilityMapEnumeratesAndCompares().ConfigureAwait(false);
        ConfidenceThresholdsChecks.DefaultThresholdsEqualTheirExplicitValues();
        DecisionErrorChecks.HandBuiltErrorCarriesItsKindAndMessage();
        QuestionFailureChecks.HandBuiltFailureEqualsTheReportedOne();
        await SystemOneModelChecks.HandBuiltQuestionsAreSent().ConfigureAwait(false);
        await SystemOneModelChecks.HandBuiltModelCardEqualsTheListedOne().ConfigureAwait(false);
        QuestionAttributeChecks.AttributesKeepWhatTheyAreGiven();
        AnswerReaderChecks.MissingAnswerNamesTheQuestion();
        DecisionOptionSetChecks.HandWrittenOptionSetMapsOptions();
        IQuestionSetChecks.ParseRunsThroughTheInterface();

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
        AllocationChecks.AnswersGet();
        AllocationChecks.PatternHelpers();
        AllocationChecks.NoulEquals();
        AllocationChecks.EvaluateRoundTripThroughDependencyInjection();
        AllocationChecks.EvaluateRoundTripThroughBoundConfiguration();
        await AllocationChecks.TelemetryOffAsynchronousTypedEvaluation().ConfigureAwait(false);
        AllocationChecks.EvaluateRoundTripWhileListening();
        AllocationChecks.TypedEvaluateRoundTripWhileListening();
        AllocationChecks.EvaluateBuiltSetRoundTripWhileListening();

        Console.WriteLine(failures == 0 ? "AOT smoke: all checks passed" : "AOT smoke: " + failures + " check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    [Covers("Minos.DecisionClient.DecisionClient(System.Net.Http.HttpClient! httpClient, Minos.DecisionClientOptions? options) -> void")]
    [Covers("Minos.DecisionClient.EvaluateAsync(Minos.SystemOneRequest! request) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.SystemOneResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.DecisionClientOptions.DecisionClientOptions() -> void")]
    [Covers("Minos.SystemOneRequest.SystemOneRequest() -> void")]
    [Covers("Minos.NoulQuestion.NoulQuestion() -> void")]
    private static async Task EvaluateParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, NoulResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer { Noul: 0.95 }, "EvaluateAsync parses a Noul answer");
    }

    private static async Task ValidationErrorCarriesDetail()
    {
        using var http = Http(HttpStatusCode.UnprocessableEntity, ValidationResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(
            result.IsFailure
                && result.Error.Kind == DecisionErrorKind.Validation
                && result.Error.Detail is { } detail
                && string.Equals(detail.GetProperty("detail").GetString(), "questions.is_urgent.instructions is required", StringComparison.Ordinal),
            "a 422 maps to Validation with its JSON detail");
    }

    [Covers("Minos.DecisionClient.ListModelsAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.ModelList!, Minos.DecisionError!>>")]
    private static async Task ListModelsReturnsModels()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(
            result.IsSuccess && result.Value.Models.Count == 1 && string.Equals(result.Value.Models[0].Name, "jev-latest", StringComparison.Ordinal),
            "ListModelsAsync parses the models");
    }

    private static async Task MalformedBodyIsInvalidResponse()
    {
        using var http = Http(HttpStatusCode.OK, "not json");
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == DecisionErrorKind.InvalidResponse, "a malformed 2xx body maps to InvalidResponse");
    }

    private static async Task OpenRouterModelListingIsUnsupported()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new DecisionClient(http, Options(DecisionProvider.OpenRouter));

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == DecisionErrorKind.Unsupported, "model listing on OpenRouter is Unsupported");
    }

    private static async Task OverloadedThenSuccessIsRetried()
    {
        var handler = new SequenceHandler(NoulResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key", InitialBackoff = TimeSpan.FromMilliseconds(10) });

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsSuccess && handler.Calls == 2, "a 503 is retried through the resilience proxy");
    }

    [Covers("static Minos.AnswerReader.EnsureStartObject(ref System.Text.Json.Utf8JsonReader reader) -> void")]
    [Covers("static Minos.AnswerReader.NextProperty(ref System.Text.Json.Utf8JsonReader reader) -> bool")]
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

    [Covers("static Minos.Criterion.Json(Minos.DecisionContent json) -> Minos.Criterion!")]
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
            "DecisionContent instructions are sent as a JSON object");
        Check(
            string.Equals(
                root.GetProperty("team").GetProperty("criteria").GetProperty("account").GetProperty("owner").GetString(),
                "identity",
                StringComparison.Ordinal),
            "a Criterion.Json description is sent as JSON");
    }

    [Covers("Minos.DecisionClient.EvaluateAsync<T>(string! state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    private static async Task TypedEvaluateAsyncParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, TriageResponse);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.RequestsCredentials.Value
                && result.Value.Team.Value == Team.Account
                && result.Value.Urgency.Value == Urgency.High,
            "EvaluateAsync<T>(string) parses typed answers over the raw, pooled-buffer path");
    }

    [Covers("Minos.DecisionClient.EvaluateAsync<T, TState>(TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    private static async Task TypedEvaluateAsyncWithTStateParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, CredentialsResponse);
        using var client = new DecisionClient(http, Options());
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);

        var result = await client.EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState).ConfigureAwait(false);

        Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value,
            "EvaluateAsync<T, TState>(state, stateTypeInfo) parses typed answers over the raw, pooled-buffer path");
    }

    [Covers("Minos.IDecisionClient.EvaluateAsync<T>(string! state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("Minos.SystemOneResponse.SystemOneResponse() -> void")]
    [Covers("Minos.NoulAnswer.NoulAnswer() -> void")]
    [Covers("Minos.ChoiceAnswer.ChoiceAnswer() -> void")]
    [Covers("Minos.ScoreAnswer.ScoreAnswer() -> void")]
    [Covers("Minos.DecisionUsage.DecisionUsage() -> void")]
    private static async Task DefaultInterfaceMethodFallbackParsesAnswers()
    {
        // DimFallbackClient implements only IDecisionClient's two abstract members, so this call runs the interface's
        // default implementation, not DecisionClient's raw, pooled-buffer override, proving the compatible, allocating
        // fallback path also compiles and runs under Native AOT.
        IDecisionClient client = new DimFallbackClient(new SystemOneResponse
        {
            Model = "jev-1.13.0",
            Answers = new Dictionary<string, Answer>(StringComparer.Ordinal)
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
            Usage = new DecisionUsage { InputTokens = 296, OutputTokens = 20 },
        });

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.RequestsCredentials.Value
                && result.Value.Team.Value == Team.Account
                && result.Value.Urgency.Value == Urgency.High,
            "the default interface method fallback parses typed answers under Native AOT");
    }

    [Covers("static Minos.QuestionSet.CreateBuilder() -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Build() -> ZeroAlloc.Results.Result<Minos.QuestionSet!, Minos.DecisionError!>")]
    [Covers("Minos.QuestionSetBuilder.Noul(string! key, Minos.DecisionContent instructions, out Minos.NoulHandle question) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Choice<T>(string! key, Minos.DecisionContent instructions, out Minos.ChoiceHandle<T> question, System.Action<Minos.ChoiceOptionsBuilder<T>!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Choice(string! key, Minos.DecisionContent instructions, out Minos.KeyedChoiceHandle question, System.Action<Minos.KeyedChoiceOptionsBuilder!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.QuestionSetBuilder.Score<T>(string! key, Minos.DecisionContent instructions, out Minos.ScoreHandle<T> question, System.Action<Minos.ScoreLevelsBuilder<T>!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.ChoiceOptionsBuilder<T>.Describe(T option, Minos.Criterion! criterion) -> Minos.ChoiceOptionsBuilder<T>!")]
    [Covers("Minos.KeyedChoiceOptionsBuilder.Option(string! key, Minos.Criterion! criterion) -> Minos.KeyedChoiceOptionsBuilder!")]
    [Covers("Minos.ScoreLevelsBuilder<T>.Level(T level, Minos.Criterion! criterion) -> Minos.ScoreLevelsBuilder<T>!")]
    [Covers("static Minos.Criterion.Text(string! description) -> Minos.Criterion!")]
    [Covers("Minos.Criterion.WithExamples(params System.ReadOnlySpan<string?> examples) -> Minos.Criterion!")]
    [Covers("Minos.DecisionClient.EvaluateAsync(Minos.QuestionSet! questionSet, Minos.DecisionContent state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.Answers!, Minos.DecisionError!>>")]
    [Covers("Minos.Answers.Get(Minos.NoulHandle question) -> Minos.Noul")]
    [Covers("Minos.Answers.Get(Minos.KeyedChoiceHandle question) -> Minos.KeyedChoice")]
    [Covers("Minos.Answers.Get<T>(Minos.ChoiceHandle<T> question) -> Minos.Choice<T>")]
    [Covers("Minos.Answers.Get<T>(Minos.ScoreHandle<T> question) -> Minos.Score<T>")]
    private static async Task BuiltQuestionSetEvaluates()
    {
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        Check(set.Warnings.Count == 0, "a question set built at run time passes its rules");

        using var http = Http(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(set, SmokeAnswers.State).ConfigureAwait(false);

        Check(
            result.IsSuccess
                && !result.Value.Get(credentials).Value
                && result.Value.Get(team).Value == Team.Account
                && string.Equals(result.Value.Get(product).Value, "pro-plan", StringComparison.Ordinal)
                && result.Value.Get(urgency).Value == Urgency.High,
            "a built question set evaluates over the raw, pooled-buffer path");

        var invalid = QuestionSet.CreateBuilder().Choice("empty", "Which one?", out _, options => { }).Build();
        Check(
            invalid.IsFailure
                && invalid.Error.Kind == DecisionErrorKind.InvalidQuestions
                && string.Equals(invalid.Error.Failures[0].Rule, "MIN001", StringComparison.Ordinal),
            "a built question set that breaks a rule fails with its MIN id");
    }

    [Covers("Minos.QuestionSetBuilder.Choice<T>(string! key, Minos.DecisionContent instructions, out Minos.ChoiceHandle<T> question) -> Minos.QuestionSetBuilder!")]
    private static async Task BuiltEnumChoiceReadsTheFieldsInDeclarationOrder()
    {
        var set = SmokeBuiltSet.AliasedChoice(out var channel);
        Check(
            string.Equals(Encoding.UTF8.GetString(set.QuestionsUtf8), SmokeBuiltSet.AliasedChoiceQuestions, StringComparison.Ordinal),
            "a built enum Choice sends its options in declaration order, the alias skipped, under Native AOT");

        using var http = Http(HttpStatusCode.OK, SmokeBuiltSet.AliasedChoiceResponseJson);
        using var client = new DecisionClient(http, Options());

        var result = await client.EvaluateAsync(set, "Sent from my phone's mail app.").ConfigureAwait(false);

        Check(
            result.IsSuccess
                && result.Value.Get(channel).Value == Channel.Email
                && Math.Abs(result.Value.Get(channel).Probabilities[Channel.Mail] - 0.8) < 1e-12,
            "a built enum Choice keys an aliased value by its first declared name, email, under Native AOT");
    }

    internal static HttpClient Http(HttpStatusCode status, string body)
        => new(new CannedHandler(status, body)) { BaseAddress = new Uri("https://example.test/api/") };

    internal static DecisionClientOptions Options(DecisionProvider provider = DecisionProvider.TypeSafe)
        => new() { ApiKey = "smoke-key", Provider = provider };

    internal static SystemOneRequest Request() => new()
    {
        State = SmokeAnswers.State,
        Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
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
