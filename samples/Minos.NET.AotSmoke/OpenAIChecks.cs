using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minos.OpenAI;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="OpenAIDecisionClient"/>, its options and <c>AddOpenAIDecisionClient</c> under Native AOT: every constructor,
/// the evaluation overloads, the registrations, and a refusal read back as <see cref="DecisionErrorKind.Refused"/>.
/// </summary>
internal static class OpenAIChecks
{
    /// <summary>The triage answers in OpenAI's response shape: a predicate, a choice and a score, one entry each.</summary>
    internal const string TriageResponse = """{"answers":[{"type":"predicate","name":"requests_credentials","probability":0.1},{"type":"choice","name":"team","choice":"account","probabilities":[{"value":"billing","probability":0.2},{"value":"account","probability":0.8}],"confidence":0.7},{"type":"score","name":"urgency","score":1.9,"probabilities":[{"value":0,"label":"0","probability":0.0},{"value":1,"label":"1","probability":0.1},{"value":2,"label":"2","probability":0.9}],"confidence":0.8}]}""";

    /// <summary>The credentials question alone, in OpenAI's response shape.</summary>
    internal const string CredentialsResponse = """{"answers":[{"type":"predicate","name":"requests_credentials","probability":0.1}]}""";

    private const string RefusalResponse = """{"answers":[{"type":"refusal","name":"requests_credentials"}]}""";

    /// <summary>Gets options with a key, so a client is built without the environment.</summary>
    internal static OpenAIDecisionClientOptions ClientOptions() => new() { ApiKey = "smoke-key" };

    /// <summary>Gets an <see cref="HttpClient"/> over a canned handler, with the base address a client over it needs.</summary>
    internal static HttpClient Http(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(new CannedHandler(status, body)) { BaseAddress = new Uri("https://example.test/v1/") };

    [Covers("Minos.OpenAI.OpenAIDecisionClient.OpenAIDecisionClient() -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClient.OpenAIDecisionClient(Minos.OpenAI.OpenAIDecisionClientOptions? options) -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClient.OpenAIDecisionClient(Minos.OpenAI.OpenAIDecisionClientOptions? options, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory) -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClientOptions.OpenAIDecisionClientOptions() -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClient.Dispose() -> void")]
    public static async Task ClientsThatOwnTheirHttpClientRefuseCallsAfterDispose()
    {
        using (SmokeAssert.NoOpenAIKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<InvalidOperationException>(() => new OpenAIDecisionClient().Dispose()),
                "new OpenAIDecisionClient() without OPENAI_API_KEY throws under Native AOT");
        }

        OpenAIDecisionClient fromEnvironment;
        using (SmokeAssert.OpenAIEnvironment())
        {
            fromEnvironment = new OpenAIDecisionClient();
        }

        var fromOptions = new OpenAIDecisionClient(ClientOptions());
        var withLogging = new OpenAIDecisionClient(ClientOptions(), NullLoggerFactory.Instance);
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        fromEnvironment.Dispose();
        fromOptions.Dispose();
        withLogging.Dispose();
        withLogging.Dispose();

        Program.Check(
            await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => fromEnvironment.EvaluateAsync(request).AsTask()).ConfigureAwait(false)
                && await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => fromOptions.EvaluateAsync(request).AsTask()).ConfigureAwait(false)
                && await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => withLogging.EvaluateAsync(request).AsTask()).ConfigureAwait(false),
            "an OpenAIDecisionClient that owns its HttpClient is built from the environment or options, disposes twice and then refuses calls, under Native AOT");
    }

    [Covers("Minos.OpenAI.OpenAIDecisionClient.OpenAIDecisionClient(System.Net.Http.HttpClient! httpClient) -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClient.OpenAIDecisionClient(System.Net.Http.HttpClient! httpClient, Minos.OpenAI.OpenAIDecisionClientOptions? options) -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClient.OpenAIDecisionClient(System.Net.Http.HttpClient! httpClient, Minos.OpenAI.OpenAIDecisionClientOptions? options, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory) -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClient.EvaluateAsync(Minos.DecisionRequest! request) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.OpenAI.OpenAIDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    public static async Task ClientsOverAnHttpClientEvaluateTheTriageAnswers()
    {
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        using var cancellation = new CancellationTokenSource();

        using var http = Http(TriageResponse);
        OpenAIDecisionClient fromEnvironment;
        using (SmokeAssert.OpenAIEnvironment())
        {
            fromEnvironment = new OpenAIDecisionClient(http);
        }

        using var optionsHttp = Http(TriageResponse);
        using var fromOptions = new OpenAIDecisionClient(optionsHttp, ClientOptions());
        using var loggingHttp = Http(TriageResponse);
        using var withLogging = new OpenAIDecisionClient(loggingHttp, ClientOptions(), NullLoggerFactory.Instance);

        using (fromEnvironment)
        {
            var first = await fromEnvironment.EvaluateAsync(request).ConfigureAwait(false);
            var second = await fromOptions.EvaluateAsync(request, cancellation.Token).ConfigureAwait(false);
            var third = await withLogging.EvaluateAsync(request).ConfigureAwait(false);

            Program.Check(
                first.IsSuccess && second.IsSuccess && third.IsSuccess,
                "the OpenAIDecisionClient constructors over an HttpClient each evaluate a DecisionRequest under Native AOT");
        }

        using var typedHttp = Http(TriageResponse);
        using var typedClient = new OpenAIDecisionClient(typedHttp, ClientOptions());
        var typed = await typedClient.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);
        Program.Check(
            SmokeAnswers.IsTriage(typed) && typed.Value.Team.Confidence == 0.7 && typed.Value.Urgency.Value == Urgency.High,
            "an OpenAIDecisionClient reads a predicate, a choice and a score into typed answers under Native AOT");
    }

    public static async Task RefusalIsRefused()
    {
        using var http = Http(RefusalResponse);
        using var client = new OpenAIDecisionClient(http, ClientOptions());

        var result = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);

        Program.Check(
            result.IsFailure
                && result.Error.Kind == DecisionErrorKind.Refused
                && string.Equals(result.Error.Message, "The model refused to answer: requests_credentials.", StringComparison.Ordinal),
            "a refusal in OpenAI's response is Refused and names the question under Native AOT");
    }

    [Covers("Minos.OpenAI.OpenAIDecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    public static void ClientAnswersItsServices()
    {
        using var http = Http(CredentialsResponse);
        using var client = new OpenAIDecisionClient(http, ClientOptions());

        var capabilities = client.GetService<DecisionCapabilities>();
        var metadata = client.GetService<DecisionClientMetadata>();

        Program.Check(
            ReferenceEquals(client.GetService<OpenAIDecisionClient>(), client)
                && capabilities is { QuestionKinds: QuestionKinds.All, Images: true, ModelListing: false }
                && metadata is { ProviderName: "openai" }
                && client.GetService(typeof(DecisionCapabilities), "key") is null,
            "an OpenAIDecisionClient answers itself, its capabilities with images, and its metadata, and no keyed service, under Native AOT");
    }

    [Covers("static Minos.OpenAI.OpenAIDecisionClient.ConfigureHttpClient(System.Net.Http.HttpClient! httpClient, Minos.OpenAI.OpenAIDecisionClientOptions? options) -> void")]
    [Covers("Minos.OpenAI.OpenAIDecisionClientOptions.Validate() -> void")]
    public static void ConfigureHttpClientAndValidateApplyTheOptions()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, CredentialsResponse));
        OpenAIDecisionClient.ConfigureHttpClient(http, new OpenAIDecisionClientOptions { Timeout = TimeSpan.FromSeconds(7) });
        using var custom = new HttpClient(new CannedHandler(HttpStatusCode.OK, CredentialsResponse));
        OpenAIDecisionClient.ConfigureHttpClient(custom, new OpenAIDecisionClientOptions { BaseAddress = new Uri("https://example.test/v1/") });

        Program.Check(
            http.BaseAddress == OpenAIDefaults.BaseAddress
                && http.Timeout == TimeSpan.FromSeconds(7)
                && custom.BaseAddress == new Uri("https://example.test/v1/")
                && OpenAIDefaults.Model.Length > 0,
            "OpenAIDecisionClient.ConfigureHttpClient applies the base address and timeout, and defaults to OpenAI's, under Native AOT");

        ClientOptions().Validate();
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => new OpenAIDecisionClientOptions { ApiKey = "smoke-key", MaxRetries = 11 }.Validate())
                && SmokeAssert.Throws<ArgumentException>(() => new OpenAIDecisionClientOptions { ApiKey = "smoke-key", Timeout = TimeSpan.Zero }.Validate()),
            "OpenAIDecisionClientOptions.Validate accepts valid options and rejects invalid ones under Native AOT");
    }

    /// <summary>Registers the default client over a canned handler.</summary>
    private static void RegisterDefaultClient(IServiceCollection services)
        => services
            .AddOpenAIDecisionClient(options =>
            {
                options.ApiKey = "smoke-key";
                options.BaseAddress = new Uri("https://example.test/v1/");
            })
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, CredentialsResponse));

    [Covers("static Microsoft.Extensions.DependencyInjection.OpenAIServiceCollectionExtensions.AddOpenAIDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, System.Action<Minos.OpenAI.OpenAIDecisionClientOptions!>! configure) -> Minos.OpenAI.OpenAIDecisionClientServiceBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.OpenAIServiceCollectionExtensions.AddOpenAIDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name, System.Action<Minos.OpenAI.OpenAIDecisionClientOptions!>! configure) -> Minos.OpenAI.OpenAIDecisionClientServiceBuilder!")]
    public static async Task DefaultAndKeyedClientsEvaluate()
    {
        var services = new ServiceCollection();
        RegisterDefaultClient(services);
        services
            .AddOpenAIDecisionClient("secondary", options =>
            {
                options.ApiKey = "smoke-secondary-key";
                options.BaseAddress = new Uri("https://secondary.example.test/v1/");
            })
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, CredentialsResponse));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IDecisionClient>().GetService<OpenAIDecisionClient>()!;
        var keyed = provider.GetRequiredKeyedService<IDecisionClient>("secondary").GetService<OpenAIDecisionClient>()!;
        var request = new DecisionRequest(SmokeStateTriage.Definition, SmokeAnswers.State);
        var result = await client.EvaluateAsync(request).ConfigureAwait(false);
        var keyedResult = await keyed.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            ReferenceEquals(client, provider.GetRequiredService<IDecisionClient>()) && !ReferenceEquals(client, keyed),
            "AddOpenAIDecisionClient registers one default client and a separate keyed client under Native AOT");
        Program.Check(
            result.IsSuccess && keyedResult.IsSuccess,
            "the default and the keyed OpenAI client each evaluate through the factory's HttpClient under Native AOT");
    }

    /// <summary>The smoke app's configuration: a default client and a secondary one, each in its own section.</summary>
    private static IConfigurationRoot BoundConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["OpenAI:ApiKey"] = "smoke-key",
                ["OpenAI:BaseAddress"] = "https://example.test/v1/",
                ["OpenAI:Timeout"] = "00:00:30",
                ["OpenAI:MaxRetries"] = "3",
                ["OpenAI:InitialBackoff"] = "00:00:00.250",
                ["OpenAI:MaxRetryDelay"] = "00:00:10",
                ["OpenAI:Jitter"] = "false",
                ["OpenAI:Model"] = "smoke-model",
                ["Secondary:ApiKey"] = "smoke-secondary-key",
                ["Secondary:BaseAddress"] = "https://secondary.example.test/v1/",
            })
            .Build();

    [Covers("static Microsoft.Extensions.DependencyInjection.OpenAIServiceCollectionExtensions.AddOpenAIDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, Microsoft.Extensions.Configuration.IConfiguration! configuration) -> Minos.OpenAI.OpenAIDecisionClientServiceBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.OpenAIServiceCollectionExtensions.AddOpenAIDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name, Microsoft.Extensions.Configuration.IConfiguration! configuration) -> Minos.OpenAI.OpenAIDecisionClientServiceBuilder!")]
    public static async Task ClientsBoundFromConfigurationEvaluate()
    {
        var configuration = BoundConfiguration();
        var services = new ServiceCollection();
        services
            .AddOpenAIDecisionClient(configuration.GetSection("OpenAI"))
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, CredentialsResponse));
        services
            .AddOpenAIDecisionClient("secondary", configuration.GetSection("Secondary"))
            .HttpClient.ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, CredentialsResponse));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<OpenAIDecisionClientOptions>>();
        var defaults = monitor.Get(Options.DefaultName);
        var keyed = monitor.Get("secondary");

        Program.Check(
            defaults is { ApiKey: "smoke-key", MaxRetries: 3, Jitter: false, Model: "smoke-model" }
                && defaults.Timeout == TimeSpan.FromSeconds(30)
                && defaults.InitialBackoff == TimeSpan.FromMilliseconds(250)
                && defaults.MaxRetryDelay == TimeSpan.FromSeconds(10)
                && defaults.BaseAddress == new Uri("https://example.test/v1/"),
            "AddOpenAIDecisionClient binds every default client setting from configuration under Native AOT");
        Program.Check(
            keyed is { ApiKey: "smoke-secondary-key" } && keyed.BaseAddress == new Uri("https://secondary.example.test/v1/"),
            "AddOpenAIDecisionClient binds a keyed client's own section under Native AOT");

        var request = new DecisionRequest(SmokeStateTriage.Definition, SmokeAnswers.State);
        var result = await provider.GetRequiredService<IDecisionClient>().EvaluateAsync(request).ConfigureAwait(false);
        var keyedResult = await provider.GetRequiredKeyedService<IDecisionClient>("secondary").EvaluateAsync(request).ConfigureAwait(false);
        Program.Check(
            result.IsSuccess && keyedResult.IsSuccess,
            "the default and the keyed OpenAI client bound from configuration each evaluate under Native AOT");
    }

    [Covers("static Microsoft.Extensions.DependencyInjection.OpenAIServiceCollectionExtensions.AddOpenAIDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> Minos.OpenAI.OpenAIDecisionClientServiceBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.OpenAIServiceCollectionExtensions.AddOpenAIDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name) -> Minos.OpenAI.OpenAIDecisionClientServiceBuilder!")]
    public static async Task ClientsConfiguredFromTheEnvironmentEvaluate()
    {
        using (SmokeAssert.OpenAIEnvironment())
        {
            var services = new ServiceCollection();
            services.AddOpenAIDecisionClient().HttpClient.ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, CredentialsResponse));
            services.AddOpenAIDecisionClient("secondary").HttpClient.ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, CredentialsResponse));
            using var provider = services.BuildServiceProvider();

            var request = new DecisionRequest(SmokeStateTriage.Definition, SmokeAnswers.State);
            var client = provider.GetRequiredService<IDecisionClient>();
            var keyed = provider.GetRequiredKeyedService<IDecisionClient>("secondary");
            var result = await client.EvaluateAsync(request).ConfigureAwait(false);
            var keyedResult = await keyed.EvaluateAsync(request).ConfigureAwait(false);

            Program.Check(
                !ReferenceEquals(client, keyed) && result.IsSuccess && keyedResult.IsSuccess,
                "AddOpenAIDecisionClient() and AddOpenAIDecisionClient(name) take the key from the environment and evaluate under Native AOT");
        }

        var unconfigured = new ServiceCollection();
        unconfigured.AddOpenAIDecisionClient();
        using var unconfiguredProvider = unconfigured.BuildServiceProvider();
        using (SmokeAssert.NoOpenAIKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<OptionsValidationException>(() => unconfiguredProvider.GetRequiredService<IDecisionClient>()),
                "AddOpenAIDecisionClient() with no key in options or the environment fails options validation under Native AOT");
        }
    }
}
