using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using static Minos.DependencyInjection.Tests.Registrations;

namespace Minos.DependencyInjection.Tests;

/// <summary>Every <c>AddDecisionClient</c> registration validates its options through <see cref="DecisionClientOptions.Validate()"/>.</summary>
public sealed class AddDecisionClientValidationTests
{
    private const string MaxRetriesMessage = "MaxRetries must be between 0 and 10. (Parameter 'options')";

    [Fact]
    public void InvalidDefaultOptions_Throw_OptionsValidationException_OnTheFirstResolve()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(Invalid);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Equal(Microsoft.Extensions.Options.Options.DefaultName, exception.OptionsName);
        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public void InvalidKeyedOptions_Throw_OptionsValidationException_OnTheFirstResolve()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("openrouter", Invalid);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredKeyedService<IDecisionClient>("openrouter"));

        Assert.Equal("openrouter", exception.OptionsName);
        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public void TheFailure_IsTheCoresOwnMessage()
    {
        var options = new DecisionClientOptions();
        Invalid(options);
        var core = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal(MaxRetriesMessage, core.Message);
    }

    [Fact]
    public void EachValidator_ChecksOnlyItsOwnName()
    {
        // A keyed client's invalid options must not fail the default client, and the reverse.
        var services = new ServiceCollection();
        services.AddDecisionClient(Options("http://default.local/"));
        services.AddDecisionClient("broken", Invalid);
        services.AddDecisionClient("valid", Options("http://valid.local/"));
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
        services.AddDecisionClient("first", Options("http://first.local/"));
        services.AddDecisionClient("second", Invalid);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("second"));
    }

    [Fact]
    public void RepeatCalls_DoNotStackValidators()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(Invalid);
        services.AddDecisionClient(Invalid);
        services.AddDecisionClient();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public async Task InvalidOptions_FailTheHost_AtStartAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddDecisionClient(Invalid);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal([MaxRetriesMessage], exception.Failures);
    }

    [Fact]
    public async Task InvalidKeyedOptions_FailTheHost_AtStartAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddDecisionClient(Options("http://default.local/"));
        builder.Services.AddDecisionClient("openrouter", Invalid);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal("openrouter", exception.OptionsName);
    }

    [Fact]
    public async Task ValidOptions_StartTheHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddDecisionClient(Options("http://default.local/"));
        builder.Services.AddDecisionClient("openrouter", Options("http://openrouter.local/"));
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    // A key and a base address, so no environment variable is read, and one invalid value.
    private static void Invalid(DecisionClientOptions options)
    {
        Options("http://invalid.local/")(options);
        options.MaxRetries = 11;
    }
}
