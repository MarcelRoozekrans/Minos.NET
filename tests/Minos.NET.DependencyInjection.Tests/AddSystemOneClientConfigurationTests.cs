using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minos.SystemOne;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary><c>AddSystemOneClient</c> with options bound from <see cref="IConfiguration"/>.</summary>
public sealed class AddSystemOneClientConfigurationTests
{
    [Fact]
    public void EveryKey_Binds()
    {
        var configuration = Configuration(
            ("Minos:Provider", "OpenRouter"),
            ("Minos:ApiKey", "bound-key"),
            ("Minos:BaseAddress", "http://bound.local/api/"),
            ("Minos:Model", "jev-1.13.0"),
            ("Minos:Timeout", "00:00:05"),
            ("Minos:MaxRetries", "4"),
            ("Minos:InitialBackoff", "00:00:00.100"),
            ("Minos:MaxRetryDelay", "00:00:03"),
            ("Minos:Jitter", "false"));
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration.GetSection("Minos"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal(SystemOneProvider.OpenRouter, options.Provider);
        Assert.Equal("bound-key", options.ApiKey);
        Assert.Equal(new Uri("http://bound.local/api/"), options.BaseAddress);
        Assert.Equal("jev-1.13.0", options.Model);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
        Assert.Equal(4, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(3), options.MaxRetryDelay);
        Assert.False(options.Jitter);
    }

    [Fact]
    public async Task BoundClient_SendsToTheBoundBaseAddress()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/api/"), ("MaxRetries", "0"));
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration).HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<SystemOneClient>()!.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(new Uri("http://bound.local/api/v1/systemone"), OnlyRequest(handler).Uri);
    }

    [Fact]
    public void PresetKeys_Bind()
    {
        var configuration = Configuration(
            ("Local:Provider", "Custom"),
            ("Local:BaseAddress", "http://local.test/api/"),
            ("Local:Path", "decide/v1/systemone"),
            ("Local:Model", "m"));
        var services = new ServiceCollection();
        services.AddSystemOneClient("local", configuration.GetSection("Local"));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>();

        var local = monitor.Get("local");

        Assert.Equal((SystemOneProvider.Custom, "decide/v1/systemone", (string?)null), (local.Provider, local.Path, local.ApiKey));
    }

    [Fact]
    public void CapabilityKeys_Bind_IncludingTheFlagsFromACommaList()
    {
        var configuration = Configuration(
            ("Local:Provider", "Custom"),
            ("Local:BaseAddress", "http://local.test/api/"),
            ("Local:Model", "m"),
            ("Local:Capabilities:QuestionKinds", "Noul, Choice"),
            ("Local:Capabilities:MaxQuestions", "8"),
            ("Local:Capabilities:MaxChoiceOptions", "16"),
            ("Local:Capabilities:Images", "true"),
            ("Local:Capabilities:ModelListing", "false"));
        var services = new ServiceCollection();
        services.AddSystemOneClient("local", configuration.GetSection("Local"));
        using var provider = services.BuildServiceProvider();

        var bound = provider.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>().Get("local").Capabilities;
        var resolved = provider.GetRequiredKeyedService<IDecisionClient>("local").GetService<DecisionCapabilities>()!;

        var expected = (QuestionKinds.Noul | QuestionKinds.Choice, 8, 16, true, false);
        Assert.Equal(expected, (bound.QuestionKinds, bound.MaxQuestions, bound.MaxChoiceOptions, bound.Images, bound.ModelListing));
        Assert.Equal(expected, (resolved.QuestionKinds, resolved.MaxQuestions, resolved.MaxChoiceOptions, resolved.Images, resolved.ModelListing));
    }

    [Fact]
    public void UnboundCapabilities_StayUnset_AndTheClientUsesThePresetDefaults()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"));
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration);
        using var provider = services.BuildServiceProvider();

        var bound = provider.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName).Capabilities;
        var resolved = provider.GetRequiredService<IDecisionClient>().GetService<DecisionCapabilities>()!;

        Assert.Equal(
            ((QuestionKinds?)null, (int?)null, (int?)null, (bool?)null, (bool?)null),
            (bound.QuestionKinds, bound.MaxQuestions, bound.MaxChoiceOptions, bound.Images, bound.ModelListing));
        Assert.Equal(
            (QuestionKinds.All, (int?)null, 255, false, true),
            (resolved.QuestionKinds, resolved.MaxQuestions, resolved.MaxChoiceOptions, resolved.Images, resolved.ModelListing));
    }

    [Fact]
    public void InvalidBoundCapability_FailsValidation_WithTheCoresMessage()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"), ("Capabilities:MaxQuestions", "0"));
        var services = new ServiceCollection();
        services.AddSystemOneClient("bound", configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("bound"));

        Assert.Equal(["Capabilities.MaxQuestions must be at least 1. (Parameter 'options')"], exception.Failures);
    }

    [Fact]
    public void UnboundModel_StaysUnset_AndTheClientUsesThePresetDefault()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"));
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Null(options.Model);
        Assert.Equal("jev-latest", provider.GetRequiredService<IDecisionClient>().GetService<DecisionClientMetadata>()!.DefaultModel);
    }

    [Fact]
    public async Task BoundCustomClient_SendsToItsPath_WithoutAKey()
    {
        var configuration = Configuration(
            ("Provider", "Custom"), ("BaseAddress", "http://local.test/api/"), ("Path", "decide/v1/systemone"), ("Model", "m"), ("MaxRetries", "0"));
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration).HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDecisionClient>().GetService<SystemOneClient>()!.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        var sent = OnlyRequest(handler);
        Assert.Equal(new Uri("http://local.test/api/decide/v1/systemone"), sent.Uri);
        Assert.Null(sent.Authorization);
    }

    [Fact]
    public void KeyedAndDefaultBindings_StaySeparate()
    {
        var configuration = Configuration(
            ("Default:ApiKey", "default-key"),
            ("Default:BaseAddress", "http://default.local/"),
            ("Default:MaxRetries", "1"),
            ("OpenRouter:Provider", "OpenRouter"),
            ("OpenRouter:ApiKey", "openrouter-key"),
            ("OpenRouter:MaxRetries", "5"));
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration.GetSection("Default"));
        services.AddSystemOneClient("openrouter", configuration.GetSection("OpenRouter"));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>();

        var defaults = monitor.Get(Microsoft.Extensions.Options.Options.DefaultName);
        var keyed = monitor.Get("openrouter");

        Assert.Equal((SystemOneProvider.TypeSafe, "default-key", 1), (defaults.Provider, defaults.ApiKey, defaults.MaxRetries));
        Assert.Equal((SystemOneProvider.OpenRouter, "openrouter-key", 5), (keyed.Provider, keyed.ApiKey, keyed.MaxRetries));
        Assert.NotSame(provider.GetRequiredService<IDecisionClient>(), provider.GetRequiredKeyedService<IDecisionClient>("openrouter"));
    }

    [Fact]
    public void ConfigureAfterBinding_OverridesTheBoundValue()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("MaxRetries", "4"));
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration);
        services.AddSystemOneClient(options => options.MaxRetries = 0);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal(0, options.MaxRetries);
        Assert.Equal("bound-key", options.ApiKey);
    }

    [Fact]
    public void InvalidBoundValue_FailsValidation_WithTheCoresMessage()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"), ("MaxRetries", "11"));
        var services = new ServiceCollection();
        services.AddSystemOneClient("bound", configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("bound"));

        Assert.Equal(["MaxRetries must be between 0 and 10. (Parameter 'options')"], exception.Failures);
    }

    [Fact]
    public void UnconvertibleBoundValue_FailsAtTheFirstResolve()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"), ("MaxRetries", "abc"));
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Contains("MaxRetries", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArguments_Throw_AtRegistration()
    {
        var services = new ServiceCollection();
        var configuration = Configuration();
        IServiceCollection? none = null;

        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddSystemOneClient(configuration)).ParamName);
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddSystemOneClient("bound", configuration)).ParamName);
        Assert.Equal("configuration", Assert.Throws<ArgumentNullException>(() => services.AddSystemOneClient((IConfiguration)null!)).ParamName);
        Assert.Equal("configuration", Assert.Throws<ArgumentNullException>(() => services.AddSystemOneClient("bound", (IConfiguration)null!)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddSystemOneClient(null!, configuration)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddSystemOneClient(string.Empty, configuration)).ParamName);
    }

    private static IConfigurationRoot Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
