using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary>Every <c>AddJevClient</c> registration validates its options through <see cref="JevClientOptions.Validate()"/>.</summary>
public sealed class AddJevClientValidationTests
{
    private const string MaxRetriesMessage = "MaxRetries must be between 0 and 10. (Parameter 'options')";

    [Fact]
    public void InvalidDefaultOptions_Throw_OptionsValidationException_OnTheFirstResolve()
    {
        var services = new ServiceCollection();
        services.AddJevClient(Invalid);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IJevClient>());

        Assert.Equal(Microsoft.Extensions.Options.Options.DefaultName, exception.OptionsName);
        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public void InvalidKeyedOptions_Throw_OptionsValidationException_OnTheFirstResolve()
    {
        var services = new ServiceCollection();
        services.AddJevClient("openrouter", Invalid);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredKeyedService<IJevClient>("openrouter"));

        Assert.Equal("openrouter", exception.OptionsName);
        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public void TheFailure_IsTheCoresOwnMessage()
    {
        var options = new JevClientOptions();
        Invalid(options);
        var core = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal(MaxRetriesMessage, core.Message);
    }

    [Fact]
    public void EachValidator_ChecksOnlyItsOwnName()
    {
        // A keyed client's invalid options must not fail the default client, and the reverse.
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/"));
        services.AddJevClient("broken", Invalid);
        services.AddJevClient("valid", Options("http://valid.local/"));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IJevClient>());
        Assert.NotNull(provider.GetRequiredKeyedService<IJevClient>("valid"));
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IJevClient>("broken"));
    }

    [Fact]
    public void EveryName_GetsAValidator()
    {
        // Validators added through TryAddEnumerable would collapse into one, leaving the second name unchecked.
        var services = new ServiceCollection();
        services.AddJevClient("first", Options("http://first.local/"));
        services.AddJevClient("second", Invalid);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IJevClient>("second"));
    }

    [Fact]
    public void RepeatCalls_DoNotStackValidators()
    {
        var services = new ServiceCollection();
        services.AddJevClient(Invalid);
        services.AddJevClient(Invalid);
        services.AddJevClient();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IJevClient>());

        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public async Task InvalidOptions_FailTheHost_AtStartAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddJevClient(Invalid);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public async Task InvalidKeyedOptions_FailTheHost_AtStartAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddJevClient(Options("http://default.local/"));
        builder.Services.AddJevClient("openrouter", Invalid);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal("openrouter", exception.OptionsName);
    }

    [Fact]
    public async Task ValidOptions_StartTheHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddJevClient(Options("http://default.local/"));
        builder.Services.AddJevClient("openrouter", Options("http://openrouter.local/"));
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    // A key and a base address, so no environment variable is read, and one invalid value.
    private static void Invalid(JevClientOptions options)
    {
        Options("http://invalid.local/")(options);
        options.MaxRetries = 11;
    }
}
