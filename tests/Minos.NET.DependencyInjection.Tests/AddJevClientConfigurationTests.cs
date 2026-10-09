using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary><c>AddJevClient</c> with options bound from <see cref="IConfiguration"/>.</summary>
public sealed class AddJevClientConfigurationTests
{
    [Fact]
    public void EveryKey_Binds()
    {
        var configuration = Configuration(
            ("Jev:Provider", "OpenRouter"),
            ("Jev:ApiKey", "bound-key"),
            ("Jev:BaseAddress", "http://bound.local/api/"),
            ("Jev:Model", "jev-1.13.0"),
            ("Jev:Timeout", "00:00:05"),
            ("Jev:MaxRetries", "4"),
            ("Jev:InitialBackoff", "00:00:00.100"),
            ("Jev:MaxRetryDelay", "00:00:03"),
            ("Jev:Jitter", "false"));
        var services = new ServiceCollection();
        services.AddJevClient(configuration.GetSection("Jev"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<JevClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal(JevProvider.OpenRouter, options.Provider);
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
        services.AddJevClient(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(new Uri("http://bound.local/api/v1/systemone"), OnlyRequest(handler).Uri);
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
        services.AddJevClient(configuration.GetSection("Default"));
        services.AddJevClient("openrouter", configuration.GetSection("OpenRouter"));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<JevClientOptions>>();

        var defaults = monitor.Get(Microsoft.Extensions.Options.Options.DefaultName);
        var keyed = monitor.Get("openrouter");

        Assert.Equal((JevProvider.TypeSafe, "default-key", 1), (defaults.Provider, defaults.ApiKey, defaults.MaxRetries));
        Assert.Equal((JevProvider.OpenRouter, "openrouter-key", 5), (keyed.Provider, keyed.ApiKey, keyed.MaxRetries));
        Assert.NotSame(provider.GetRequiredService<IJevClient>(), provider.GetRequiredKeyedService<IJevClient>("openrouter"));
    }

    [Fact]
    public void ConfigureAfterBinding_OverridesTheBoundValue()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("MaxRetries", "4"));
        var services = new ServiceCollection();
        services.AddJevClient(configuration);
        services.AddJevClient(options => options.MaxRetries = 0);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<JevClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal(0, options.MaxRetries);
        Assert.Equal("bound-key", options.ApiKey);
    }

    [Fact]
    public void InvalidBoundValue_FailsValidation_WithTheCoresMessage()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"), ("MaxRetries", "11"));
        var services = new ServiceCollection();
        services.AddJevClient("bound", configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IJevClient>("bound"));

        Assert.Equal(["MaxRetries must be between 0 and 10. (Parameter 'options')"], exception.Failures);
    }

    [Fact]
    public void UnconvertibleBoundValue_FailsAtTheFirstResolve()
    {
        var configuration = Configuration(("ApiKey", "bound-key"), ("BaseAddress", "http://bound.local/"), ("MaxRetries", "abc"));
        var services = new ServiceCollection();
        services.AddJevClient(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IJevClient>());

        Assert.Contains("MaxRetries", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArguments_Throw_AtRegistration()
    {
        var services = new ServiceCollection();
        var configuration = Configuration();
        IServiceCollection? none = null;

        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddJevClient(configuration)).ParamName);
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddJevClient("bound", configuration)).ParamName);
        Assert.Equal("configuration", Assert.Throws<ArgumentNullException>(() => services.AddJevClient((IConfiguration)null!)).ParamName);
        Assert.Equal("configuration", Assert.Throws<ArgumentNullException>(() => services.AddJevClient("bound", (IConfiguration)null!)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddJevClient(null!, configuration)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddJevClient(string.Empty, configuration)).ParamName);
    }

    private static IConfigurationRoot Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
