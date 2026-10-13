using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minos.SystemOne;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary>Every <c>AddSystemOneDecisionClient</c> registration validates its options through <see cref="SystemOneDecisionClientOptions.Validate()"/>.</summary>
public sealed class AddSystemOneDecisionClientValidationTests
{
    private const string MaxRetriesMessage = "MaxRetries must be between 0 and 10. (Parameter 'options')";

    [Fact]
    public void InvalidDefaultOptions_Throw_OptionsValidationException_OnTheFirstResolve()
    {
        var services = new ServiceCollection();
        services.AddSystemOneDecisionClient(Invalid);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Equal(Microsoft.Extensions.Options.Options.DefaultName, exception.OptionsName);
        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public void InvalidKeyedOptions_Throw_OptionsValidationException_OnTheFirstResolve()
    {
        var services = new ServiceCollection();
        services.AddSystemOneDecisionClient("openrouter", Invalid);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredKeyedService<IDecisionClient>("openrouter"));

        Assert.Equal("openrouter", exception.OptionsName);
        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public void TheFailure_IsTheCoresOwnMessage()
    {
        var options = new SystemOneDecisionClientOptions();
        Invalid(options);
        var core = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal(MaxRetriesMessage, core.Message);
    }

    [Fact]
    public void EachValidator_ChecksOnlyItsOwnName()
    {
        // A keyed client's invalid options must not fail the default client, and the reverse.
        var services = new ServiceCollection();
        services.AddSystemOneDecisionClient(Options("http://default.local/"));
        services.AddSystemOneDecisionClient("broken", Invalid);
        services.AddSystemOneDecisionClient("valid", Options("http://valid.local/"));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDecisionClient>());
        Assert.NotNull(provider.GetRequiredKeyedService<IDecisionClient>("valid"));
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("broken"));
    }

    [Fact]
    public void EveryName_GetsAValidator()
    {
        // Validators added through TryAddEnumerable would collapse into one, leaving the second name unchecked.
        var services = new ServiceCollection();
        services.AddSystemOneDecisionClient("first", Options("http://first.local/"));
        services.AddSystemOneDecisionClient("second", Invalid);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("second"));
    }

    [Fact]
    public void RepeatCalls_DoNotStackValidators()
    {
        var services = new ServiceCollection();
        services.AddSystemOneDecisionClient(Invalid);
        services.AddSystemOneDecisionClient(Invalid);
        services.AddSystemOneDecisionClient();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public async Task InvalidOptions_FailTheHost_AtStartAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSystemOneDecisionClient(Invalid);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public async Task InvalidKeyedOptions_FailTheHost_AtStartAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSystemOneDecisionClient(Options("http://default.local/"));
        builder.Services.AddSystemOneDecisionClient("openrouter", Invalid);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal("openrouter", exception.OptionsName);
    }

    [Fact]
    public async Task ValidOptions_StartTheHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSystemOneDecisionClient(Options("http://default.local/"));
        builder.Services.AddSystemOneDecisionClient("openrouter", Options("http://openrouter.local/"));
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    // A key and a base address, so no environment variable is read, and one invalid value.
    private static void Invalid(SystemOneDecisionClientOptions options)
    {
        Options("http://invalid.local/")(options);
        options.MaxRetries = 11;
    }
}
