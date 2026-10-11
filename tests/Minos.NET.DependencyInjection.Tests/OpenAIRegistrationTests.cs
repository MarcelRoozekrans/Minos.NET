using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minos.OpenAI;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary><c>AddOpenAIDecisionClient</c>: the same registration as <c>AddSystemOneClient</c>, for OpenAI's Decisions API.</summary>
public sealed class OpenAIRegistrationTests
{
    private const string Answer = """{"answers":[{"type":"predicate","name":"is_urgent","probability":0.9}]}""";

    [Fact]
    public void DefaultClient_IsOneSingleton()
    {
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient(OpenAIOptions("http://default.local/"));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.IsType<OpenAIDecisionClient>(client);
        Assert.Same(client, provider.GetRequiredService<IDecisionClient>());
        Assert.Equal("openai", client.GetService<DecisionClientMetadata>()!.ProviderName);
    }

    [Fact]
    public async Task DefaultClient_SendsThroughTheFactoryHttpClient_ToV1Decisions()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Answer);
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient(OpenAIOptions("http://default.local/api/v1")).HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDecisionClient>().EvaluateAsync(OneNoul(), "Help!");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var request = OnlyRequest(handler);
        Assert.Equal(new Uri("http://default.local/api/v1/decisions"), request.Uri);
        Assert.Equal("Bearer di-key", request.Authorization);
        Assert.Matches(UserAgentPattern, request.UserAgent);
    }

    [Fact]
    public void KeyedClient_IsOneSingleton_AndNotTheDefault()
    {
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient(OpenAIOptions("http://default.local/"));
        services.AddOpenAIDecisionClient("vision", OpenAIOptions("http://keyed.local/"));
        using var provider = services.BuildServiceProvider();

        var keyed = provider.GetRequiredKeyedService<IDecisionClient>("vision");

        Assert.IsType<OpenAIDecisionClient>(keyed);
        Assert.Same(keyed, provider.GetRequiredKeyedService<IDecisionClient>("vision"));
        Assert.NotSame(provider.GetRequiredService<IDecisionClient>(), keyed);
        Assert.Equal(new Uri("http://keyed.local/"), keyed.GetService<DecisionClientMetadata>()!.Endpoint);
    }

    [Fact]
    public void EveryKey_Binds_IncludingCapabilities()
    {
        var configuration = Configuration(
            ("Minos:ApiKey", "bound-key"),
            ("Minos:BaseAddress", "http://bound.local/api/"),
            ("Minos:Model", "gpt-6"),
            ("Minos:Timeout", "00:00:05"),
            ("Minos:MaxRetries", "4"),
            ("Minos:InitialBackoff", "00:00:00.100"),
            ("Minos:MaxRetryDelay", "00:00:03"),
            ("Minos:Jitter", "false"),
            ("Minos:UseStandardPipeline", "false"),
            ("Minos:Capabilities:QuestionKinds", "Noul, Choice"),
            ("Minos:Capabilities:MaxQuestions", "8"),
            ("Minos:Capabilities:MaxChoiceOptions", "16"),
            ("Minos:Capabilities:Images", "false"),
            ("Minos:Capabilities:ModelListing", "false"));
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient(configuration.GetSection("Minos"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<OpenAIDecisionClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);
        var resolved = provider.GetRequiredService<IDecisionClient>().GetService<DecisionCapabilities>()!;

        Assert.Equal("bound-key", options.ApiKey);
        Assert.Equal(new Uri("http://bound.local/api/"), options.BaseAddress);
        Assert.Equal("gpt-6", options.Model);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
        Assert.Equal(4, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(3), options.MaxRetryDelay);
        Assert.False(options.Jitter);
        Assert.False(options.UseStandardPipeline);
        var bound = options.Capabilities;
        var expected = (QuestionKinds.Noul | QuestionKinds.Choice, 8, 16, false, false);
        Assert.Equal(expected, (bound.QuestionKinds, bound.MaxQuestions, bound.MaxChoiceOptions, bound.Images, bound.ModelListing));
        Assert.Equal(expected, (resolved.QuestionKinds, resolved.MaxQuestions, resolved.MaxChoiceOptions, resolved.Images, resolved.ModelListing));
    }

    [Fact]
    public void UnboundCapabilities_StayUnset_AndTheClientUsesOpenAIsDefaults()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"));
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient("bound", configuration);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredKeyedService<IDecisionClient>("bound").GetService<DecisionCapabilities>()!;

        Assert.Equal(
            (QuestionKinds.All, (int?)null, (int?)null, true, false),
            (resolved.QuestionKinds, resolved.MaxQuestions, resolved.MaxChoiceOptions, resolved.Images, resolved.ModelListing));
        Assert.Equal("gpt-6-luna", provider.GetRequiredKeyedService<IDecisionClient>("bound").GetService<DecisionClientMetadata>()!.DefaultModel);
    }

    [Fact]
    public void InvalidOptions_Throw_OptionsValidationException_WithTheCoresMessage()
    {
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient("broken", options =>
        {
            OpenAIOptions("http://broken.local/")(options);
            options.MaxRetries = 11;
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("broken"));

        Assert.Equal("broken", exception.OptionsName);
        Assert.Equal(["MaxRetries must be between 0 and 10. (Parameter 'options')"], exception.Failures);
    }

    [Fact]
    public async Task ValidOptions_StartTheHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddOpenAIDecisionClient(OpenAIOptions("http://default.local/"));
        builder.Services.AddOpenAIDecisionClient("vision", OpenAIOptions("http://vision.local/"));
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void ARepeatCall_ReturnsTheSameBuilder_AndRegistersOneClient()
    {
        var services = new ServiceCollection();

        var first = services.AddOpenAIDecisionClient(OpenAIOptions("http://default.local/"));
        var second = services.AddOpenAIDecisionClient(options => options.Model = "gpt-6");
        var keyed = services.AddOpenAIDecisionClient("vision");

        Assert.Same(first, second);
        Assert.NotSame(first, keyed);
        Assert.Null(first.Name);
        Assert.Equal("vision", keyed.Name);
        Assert.NotNull(first.HttpClient);
        Assert.Equal(1, services.Count(descriptor => descriptor.ServiceType == typeof(IDecisionClient) && !descriptor.IsKeyedService));
    }

    [Fact]
    public void TheDefaultName_HeldBySystemOne_Throws()
    {
        var services = new ServiceCollection();
        services.AddSystemOneClient(Options("http://default.local/"));

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddOpenAIDecisionClient(OpenAIOptions("http://default.local/")));

        Assert.Equal("The default decision client is already registered with a different provider.", exception.Message);
    }

    [Fact]
    public void TheDefaultName_HeldByOpenAI_Throws_ForSystemOne()
    {
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient(OpenAIOptions("http://default.local/"));

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddSystemOneClient(Options("http://default.local/")));

        Assert.Equal("The default decision client is already registered with a different provider.", exception.Message);
    }

    [Fact]
    public void AKeyedOpenAIClient_BesideADefaultSystemOneClient_Works()
    {
        var services = new ServiceCollection();
        services.AddSystemOneClient(Options("http://typesafe.local/"));
        services.AddOpenAIDecisionClient("openai", OpenAIOptions("http://openai.local/"));
        using var provider = services.BuildServiceProvider();

        var systemOne = provider.GetRequiredService<IDecisionClient>();
        var openAI = provider.GetRequiredKeyedService<IDecisionClient>("openai");

        Assert.IsType<Minos.SystemOne.SystemOneClient>(systemOne);
        Assert.IsType<OpenAIDecisionClient>(openAI);
        Assert.Equal("typesafe", systemOne.GetService<DecisionClientMetadata>()!.ProviderName);
        Assert.Equal("openai", openAI.GetService<DecisionClientMetadata>()!.ProviderName);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        IServiceCollection? none = null;
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => none!.AddOpenAIDecisionClient());
        Assert.Throws<ArgumentNullException>(() => services.AddOpenAIDecisionClient((Action<OpenAIDecisionClientOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddOpenAIDecisionClient((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddOpenAIDecisionClient((string)null!));
        Assert.Throws<ArgumentException>(() => services.AddOpenAIDecisionClient(string.Empty));
        Assert.Empty(services);
    }

    private static Action<OpenAIDecisionClientOptions> OpenAIOptions(string baseAddress, string apiKey = "di-key")
        => options =>
        {
            options.ApiKey = apiKey;
            options.BaseAddress = new Uri(baseAddress);
            options.MaxRetries = 0;
        };

    private static QuestionSet OneNoul() => QuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out _).Build().Value;

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
}

/// <summary><c>AddOpenAIDecisionClient</c> with the API key environment variable unset.</summary>
[Collection(ProcessEnvironment.Name)]
public sealed class OpenAIRegistrationEnvironmentTests
{
    private const string MissingKey =
        "An OpenAI API key is required: set OpenAIDecisionClientOptions.ApiKey or the OPENAI_API_KEY environment variable.";

    [Fact]
    public async Task AMissingKey_FailsTheHost_AtStartAsync()
    {
        using var environment = new EnvironmentVariables(("OPENAI_API_KEY", null));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddOpenAIDecisionClient();
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal([MissingKey], exception.Failures);
    }

    [Fact]
    public void TheKeyFromTheEnvironment_PassesValidation()
    {
        using var environment = new EnvironmentVariables(("OPENAI_API_KEY", "env-key"), ("OPENAI_BASE_URL", null));
        var services = new ServiceCollection();
        services.AddOpenAIDecisionClient();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.Equal(new Uri("https://api.openai.com/v1/"), client.GetService<DecisionClientMetadata>()!.Endpoint);
    }
}
