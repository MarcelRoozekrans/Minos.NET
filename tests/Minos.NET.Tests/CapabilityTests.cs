using System.Net;
using Microsoft.Extensions.Logging;
using Minos.SystemOne;
using Minos.SystemOne.Transport;
using Minos.Transport;

namespace Minos.Tests;

/// <summary>The capability flags: the preset defaults, the overrides and the check that runs before a request is sent.</summary>
public sealed class CapabilityTests : IDisposable
{
    private static readonly QuestionSetDefinition NoulAndChoice = new(
        QuestionDefinition.Noul("refund", "Does the customer ask for a refund?"),
        QuestionDefinition.Choice(
            "topic",
            "What is the topic?",
            new OptionDefinition("billing", "Payments"),
            new OptionDefinition("shipping", "Delivery"),
            new OptionDefinition("other", "Anything else")));

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public void A_supported_set_passes()
        => Assert.Null(Check(SystemOneCapabilityDefaults.For(SystemOneProvider.TypeSafe), NoulAndChoice, SystemOneProvider.TypeSafe));

    [Fact]
    public void An_unsupported_kind_is_Unsupported_and_names_provider_and_question()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { QuestionKinds = QuestionKinds.Noul });

        var error = Check(caps, NoulAndChoice, SystemOneProvider.Custom);

        Assert.NotNull(error);
        Assert.Equal(DecisionErrorKind.Unsupported, error.Kind);
        Assert.Equal("Custom does not support Choice questions.", error.Message);
        var failure = OnlyFailure(error);
        Assert.Equal(("MIN201", "topic"), (failure.Rule, failure.QuestionKey));
        Assert.Equal(error.Message, failure.Message);
    }

    [Fact]
    public void An_unsupported_Score_or_Noul_kind_is_named_too()
    {
        var set = new QuestionSetDefinition(
            QuestionDefinition.Noul("refund", "Does the customer ask for a refund?"),
            QuestionDefinition.Score("urgency", "How urgent is it?", "Low", "High"));
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.VllmSemanticRouter).With(new DecisionCapabilities { QuestionKinds = QuestionKinds.Choice });

        var error = Check(caps, set, SystemOneProvider.VllmSemanticRouter);

        Assert.Equal(
            ["VllmSemanticRouter does not support Noul questions.", "VllmSemanticRouter does not support Score questions."],
            error!.Failures.Select(f => f.Message));
        Assert.Equal(["refund", "urgency"], error.Failures.Select(f => f.QuestionKey));
    }

    [Fact]
    public void Too_many_questions_is_one_set_level_failure()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { MaxQuestions = 1 });

        var error = Check(caps, NoulAndChoice, SystemOneProvider.Custom)!;

        var failure = OnlyFailure(error);
        Assert.Equal(("MIN202", (string?)null), (failure.Rule, failure.QuestionKey));
        Assert.Equal("Custom accepts at most 1 question per request; the set has 2.", error.Message);
    }

    [Fact]
    public void Too_many_questions_pluralises_a_maximum_above_one()
    {
        var set = new QuestionSetDefinition(
            QuestionDefinition.Noul("a", "A?"), QuestionDefinition.Noul("b", "B?"), QuestionDefinition.Noul("c", "C?"));
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { MaxQuestions = 2 });

        Assert.Equal("Custom accepts at most 2 questions per request; the set has 3.", Check(caps, set, SystemOneProvider.Custom)!.Message);
    }

    [Fact]
    public void Exactly_the_maximums_pass()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { MaxQuestions = 2, MaxChoiceOptions = 3 });

        Assert.Null(Check(caps, NoulAndChoice, SystemOneProvider.Custom));
    }

    [Fact]
    public void Too_many_choice_options_names_the_question()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { MaxChoiceOptions = 2 });

        var error = Check(caps, NoulAndChoice, SystemOneProvider.Custom)!;

        var failure = OnlyFailure(error);
        Assert.Equal(("MIN203", "topic"), (failure.Rule, failure.QuestionKey));
        Assert.Equal("Custom accepts at most 2 options per Choice question; 'topic' has 3.", error.Message);
    }

    [Fact]
    public void Score_levels_are_not_Choice_options()
    {
        var set = new QuestionSetDefinition(QuestionDefinition.Score("urgency", "How urgent is it?", "Low", "Medium", "High"));
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { MaxChoiceOptions = 2 });

        Assert.Null(Check(caps, set, SystemOneProvider.Custom));
    }

    [Fact]
    public void Every_failure_is_listed_and_the_message_is_the_first()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { QuestionKinds = QuestionKinds.Score, MaxQuestions = 1 });

        var error = Check(caps, NoulAndChoice, SystemOneProvider.Custom);

        Assert.Equal(["MIN202", "MIN201", "MIN201"], error!.Failures.Select(f => f.Rule));
        Assert.Equal(error.Failures[0].Message, error.Message);
    }

    [Fact]
    public void A_question_breaking_two_rules_lists_the_kind_before_the_option_count()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.Custom).With(new DecisionCapabilities { QuestionKinds = QuestionKinds.Noul, MaxChoiceOptions = 2 });

        var error = Check(caps, NoulAndChoice, SystemOneProvider.Custom);

        Assert.Equal([("MIN201", "topic"), ("MIN203", "topic")], error!.Failures.Select(f => (f.Rule, f.QuestionKey)));
    }

    [Theory]
    [InlineData(SystemOneProvider.TypeSafe, true)]
    [InlineData(SystemOneProvider.OpenRouter, false)]
    [InlineData(SystemOneProvider.VllmSemanticRouter, false)]
    [InlineData(SystemOneProvider.Custom, false)]
    public void Model_listing_follows_the_preset(SystemOneProvider provider, bool expected)
        => Assert.Equal(expected, SystemOneCapabilityDefaults.For(provider).ModelListing);

    [Fact]
    public void Every_preset_has_the_spec_defaults()
    {
        Assert.Equal(new CapabilitySet(QuestionKinds.All, null, 255, false, true), SystemOneCapabilityDefaults.For(SystemOneProvider.TypeSafe));
        Assert.Equal(new CapabilitySet(QuestionKinds.All, null, 255, false, false), SystemOneCapabilityDefaults.For(SystemOneProvider.OpenRouter));
        Assert.Equal(new CapabilitySet(QuestionKinds.All, null, null, false, false), SystemOneCapabilityDefaults.For(SystemOneProvider.VllmSemanticRouter));
        Assert.Equal(new CapabilitySet(QuestionKinds.All, null, null, false, false), SystemOneCapabilityDefaults.For(SystemOneProvider.Custom));
    }

    [Fact]
    public void Set_overrides_win_and_unset_ones_keep_the_preset_default()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.OpenRouter).With(new DecisionCapabilities { MaxQuestions = 8, ModelListing = true });

        Assert.Equal(new CapabilitySet(QuestionKinds.All, 8, 255, false, true), caps);
    }

    [Fact]
    public void Every_override_can_be_set()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.TypeSafe).With(new DecisionCapabilities
        {
            QuestionKinds = QuestionKinds.Noul | QuestionKinds.Score,
            MaxQuestions = 3,
            MaxChoiceOptions = 4,
            Images = true,
            ModelListing = false,
        });

        Assert.Equal(new CapabilitySet(QuestionKinds.Noul | QuestionKinds.Score, 3, 4, true, false), caps);
    }

    [Fact]
    public void ToOptions_is_a_fresh_fully_set_copy()
    {
        var caps = SystemOneCapabilityDefaults.For(SystemOneProvider.OpenRouter);

        var options = caps.ToOptions();

        Assert.Equal(
            (QuestionKinds.All, (int?)null, (int?)255, (bool?)false, (bool?)false),
            (options.QuestionKinds, options.MaxQuestions, options.MaxChoiceOptions, options.Images, options.ModelListing));
        Assert.NotSame(options, caps.ToOptions());
    }

    [Theory]
    [InlineData(QuestionKinds.None, "Capabilities.QuestionKinds must allow at least one question kind.")]
    [InlineData((QuestionKinds)8, "Capabilities.QuestionKinds has an unknown flag.")]
    [InlineData(QuestionKinds.Noul | (QuestionKinds)16, "Capabilities.QuestionKinds has an unknown flag.")]
    public void An_invalid_kinds_override_is_rejected(QuestionKinds kinds, string message)
        => AssertRejected(new DecisionCapabilities { QuestionKinds = kinds }, message);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_question_maximum_below_one_is_rejected(int max)
        => AssertRejected(new DecisionCapabilities { MaxQuestions = max }, "Capabilities.MaxQuestions must be at least 1.");

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void An_option_maximum_below_two_is_rejected(int max)
        => AssertRejected(new DecisionCapabilities { MaxChoiceOptions = max }, "Capabilities.MaxChoiceOptions must be at least 2.");

    [Fact]
    public void The_smallest_valid_overrides_resolve()
    {
        var settings = SystemOneClientSettings.Resolve(
            new SystemOneClientOptions
            {
                ApiKey = "k",
                Capabilities = new() { QuestionKinds = QuestionKinds.Score, MaxQuestions = 1, MaxChoiceOptions = 2 },
            },
            _ => null);

        Assert.Equal(new CapabilitySet(QuestionKinds.Score, 1, 2, false, true), settings.Capabilities);
    }

    [Fact]
    public void Null_capabilities_resolve_to_the_preset_defaults()
    {
        var settings = SystemOneClientSettings.Resolve(new SystemOneClientOptions { ApiKey = "k", Capabilities = null! }, _ => null);

        Assert.Equal(SystemOneCapabilityDefaults.For(SystemOneProvider.TypeSafe), settings.Capabilities);
    }

    [Fact]
    public async Task A_client_returns_Unsupported_without_sending()
    {
        var handler = Noul();
        using var client = Custom(handler, QuestionKinds.Noul);

        var result = await client.EvaluateAsync(new DecisionRequest(NoulAndChoice, "a ticket"));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal("Custom does not support Choice questions.", result.Error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_retry_stage_does_not_retry_Unsupported()
    {
        // The check runs in the transport, below the retry stage, so the handler sees no request either way. The retry
        // stage logs AttemptRetrying, 1003, before each retry, so its absence is what shows Unsupported is not retried.
        using var logs = new LogCapture();
        var handler = Noul();
        using var client = Custom(handler, QuestionKinds.Noul, maxRetries: 2, logs.Factory);

        var result = await client.EvaluateAsync(new DecisionRequest(NoulAndChoice, "a ticket"));

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Empty(handler.Requests);
        Assert.Equal("Unsupported", LogAssert.Field(logs.Only(1002), "ErrorKind"));
        Assert.DoesNotContain(1003, logs.EventIds);
    }

    [Fact]
    public async Task A_supported_set_is_sent()
    {
        var handler = Noul();
        using var client = Custom(handler, QuestionKinds.Noul);

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(QuestionDefinition.Noul("refund", "Refund?")), "a ticket"));

        Assert.True(result.IsSuccess);
        _ = ClientTestKit.OnlyRequest(handler);
    }

    [Fact]
    public async Task The_raw_call_is_not_checked()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-choice.json"));
        using var client = Custom(handler, QuestionKinds.Noul);

        var result = await client.EvaluateAsync(new SystemOneRequest
        {
            Model = "m",
            State = "a ticket",
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
            {
                ["department"] = new ChoiceQuestion
                {
                    Instructions = "Which?",
                    Criteria = new Dictionary<string, DecisionContent?>(StringComparer.Ordinal) { ["billing"] = null, ["technical"] = null, ["sales"] = null },
                },
            },
        });

        Assert.True(result.IsSuccess);
        _ = ClientTestKit.OnlyRequest(handler);
    }

    [Fact]
    public void GetService_DecisionCapabilities_ReturnsTheResolvedSet()
    {
        using var client = Custom(Noul(), QuestionKinds.Noul);

        var first = client.GetService<DecisionCapabilities>()!;
        var second = client.GetService<DecisionCapabilities>()!;

        Assert.Equal(
            (QuestionKinds.Noul, (int?)null, (int?)null, (bool?)false, (bool?)false),
            (first.QuestionKinds, first.MaxQuestions, first.MaxChoiceOptions, first.Images, first.ModelListing));
        Assert.NotSame(first, second);
        first.MaxQuestions = 1;
        Assert.Null(client.GetService<DecisionCapabilities>()!.MaxQuestions);
    }

    [Theory]
    [InlineData(SystemOneProvider.TypeSafe)]
    [InlineData(SystemOneProvider.OpenRouter)]
    [InlineData(SystemOneProvider.VllmSemanticRouter)]
    [InlineData(SystemOneProvider.Custom)]
    public async Task Images_OnAPresetWithoutImages_IsMIN206(SystemOneProvider provider)
    {
        // MaxRetries = 2: the retry stage logs AttemptRetrying, 1003, before each retry, so its absence shows no retry.
        using var logs = new LogCapture();
        var handler = Noul();
        using var client = ClientTestKit.Client(
            _httpClients,
            handler,
            provider: provider,
            loggerFactory: logs.Factory,
            configure: options =>
            {
                Hosted(provider)(options);
                options.MaxRetries = 2;
                options.InitialBackoff = TimeSpan.FromMilliseconds(1);
                options.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
            });

        var result = await client.EvaluateAsync<MinimalUrgencyCheck>("a ticket", [Png]);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(
            [("MIN206", (string?)null, provider.ToString() + " does not support images.")],
            result.Error.Failures.Select(f => (f.Rule, f.QuestionKey, f.Message)));
        Assert.Equal(provider.ToString() + " does not support images.", result.Error.Message);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain(1003, logs.EventIds);
    }

    [Fact]
    public async Task Images_WithImagesTrue_IsSent()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"model":"m","answers":{"is_urgent":{"type":"noul","noul":0.9}}}""");
        using var client = ClientTestKit.Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.OpenRouter,
            configure: options => options.Capabilities = new() { Images = true });

        var result = await client.EvaluateAsync<MinimalUrgencyCheck>("a ticket", [Png]);

        Assert.True(result.IsSuccess);
        _ = ClientTestKit.OnlyRequest(handler);
    }

    [Fact]
    public async Task Images_AndTooManyQuestions_ReportsBoth_InOrder()
    {
        var handler = Noul();
        using var client = ClientTestKit.Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.Custom,
            configure: options =>
            {
                Hosted(SystemOneProvider.Custom)(options);
                options.Capabilities.MaxQuestions = 1;
            });

        var result = await client.EvaluateAsync(new DecisionRequest(NoulAndChoice, "a ticket") { Images = [Png] });

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(["MIN202", "MIN206"], result.Error.Failures.Select(f => f.Rule));
        Assert.Equal(result.Error.Failures[0].Message, result.Error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Refused_IsNotTransient()
        => Assert.False(DecisionRetryOptions.IsTransient(new DecisionError(DecisionErrorKind.Refused, "x")));

    [Fact]
    public void GetService_with_a_key_does_not_return_the_capabilities()
    {
        using var client = Custom(Noul(), QuestionKinds.Noul);

        Assert.Null(client.GetService(typeof(DecisionCapabilities), "key"));
    }

    [Theory]
    [InlineData(SystemOneProvider.VllmSemanticRouter, "Model listing is not available on VllmSemanticRouter.")]
    [InlineData(SystemOneProvider.OpenRouter, "Model listing is not available on OpenRouter.")]
    [InlineData(SystemOneProvider.Custom, "Model listing is not available on Custom.")]
    public async Task ListModels_without_ModelListing_is_Unsupported_without_a_request(SystemOneProvider provider, string message)
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, provider: provider, configure: Hosted(provider));

        var result = await client.ListModelsAsync();

        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(message, result.Error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListModels_on_Custom_with_listing_on_sends_GET_v1_models()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json"));
        using var client = ClientTestKit.Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.Custom,
            configure: options =>
            {
                Hosted(SystemOneProvider.Custom)(options);
                options.Capabilities.ModelListing = true;
            });

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        var sent = ClientTestKit.OnlyRequest(handler);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(new Uri("http://local.test/api/v1/models"), sent.Uri);
    }

    [Fact]
    public async Task A_ModelListing_override_turns_listing_on()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("models.json"));
        using var client = ClientTestKit.Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.VllmSemanticRouter,
            configure: options =>
            {
                Hosted(SystemOneProvider.VllmSemanticRouter)(options);
                options.Capabilities.ModelListing = true;
            });

        Assert.True((await client.ListModelsAsync()).IsSuccess);
        _ = ClientTestKit.OnlyRequest(handler);
    }

    private static DecisionImage Png => DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");

    // The pre-send check as the transport runs it, without images.
    private static DecisionError? Check(CapabilitySet caps, QuestionSetDefinition definition, SystemOneProvider provider)
        => caps.Collect(definition, 0, provider.ToString()) is { } failures ? CapabilitySet.Unsupported(failures) : null;

    private static QuestionFailure OnlyFailure(DecisionError error)
    {
        QuestionFailure? only = null;
        Assert.Collection(error.Failures, failure => only = failure);
        return only!;
    }

    private static void AssertRejected(DecisionCapabilities capabilities, string message)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SystemOneClientSettings.Resolve(new SystemOneClientOptions { ApiKey = "k", Capabilities = capabilities }, _ => null));

        Assert.Equal("options", exception.ParamName);
        Assert.Equal(message + " (Parameter 'options')", exception.Message);
    }

    private static Action<SystemOneClientOptions> Hosted(SystemOneProvider provider) => options =>
    {
        if (provider is SystemOneProvider.VllmSemanticRouter or SystemOneProvider.Custom)
        {
            options.BaseAddress = new Uri("http://local.test/api/");
        }
    };

    private static StubHandler Noul()
        => StubHandler.Json(HttpStatusCode.OK, """{"model":"m","answers":{"refund":{"type":"noul","noul":0.9}}}""");

    private SystemOneClient Custom(StubHandler handler, QuestionKinds kinds, int maxRetries = 0, ILoggerFactory? loggerFactory = null)
        => ClientTestKit.Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.Custom,
            loggerFactory: loggerFactory,
            configure: options =>
            {
                options.BaseAddress = new Uri("http://local.test/api/");
                options.Capabilities.QuestionKinds = kinds;
                options.MaxRetries = maxRetries;
                options.InitialBackoff = TimeSpan.FromMilliseconds(1);
                options.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
            });
}
