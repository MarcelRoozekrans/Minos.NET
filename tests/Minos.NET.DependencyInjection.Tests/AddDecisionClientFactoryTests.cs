using Microsoft.Extensions.DependencyInjection;
using Minos.SystemOne;

namespace Minos.DependencyInjection.Tests;

/// <summary>The provider-neutral <c>AddDecisionClient</c> factory registrations, and how they meet <c>AddSystemOneClient</c>.</summary>
public sealed class AddDecisionClientFactoryTests
{
    [Fact]
    public void Factory_registration_resolves_the_factory_client_through_the_pipeline()
    {
        var inner = new FakeDecisionClient();
        var services = new ServiceCollection();
        services.AddDecisionClient(_ => inner).Use((request, next, ct) => next.EvaluateAsync(request, ct));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.NotSame(inner, client);
        Assert.Same(inner, client.GetService<FakeDecisionClient>());
    }

    [Fact]
    public void Keyed_factory_registration_is_keyed()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("local", _ => new FakeDecisionClient());

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetKeyedService<IDecisionClient>("local"));
        Assert.Null(provider.GetService<IDecisionClient>());
    }

    [Fact]
    public void A_repeat_call_returns_the_first_builder()
    {
        var services = new ServiceCollection();
        var first = services.AddDecisionClient(_ => new FakeDecisionClient());
        var second = services.AddDecisionClient(_ => new FakeDecisionClient());

        Assert.Same(first, second);
    }

    [Fact]
    public void Mixing_a_factory_and_a_SystemOne_registration_under_one_name_throws()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("x", _ => new FakeDecisionClient());

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddSystemOneClient("x", o => o.ApiKey = "k"));
        Assert.Equal("The decision client 'x' is already registered with a different provider.", exception.Message);
    }

    [Fact]
    public void Mixing_a_SystemOne_and_a_factory_registration_for_the_default_client_throws()
    {
        var services = new ServiceCollection();
        services.AddSystemOneClient(o => o.ApiKey = "k");

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddDecisionClient(_ => new FakeDecisionClient()));
        Assert.Equal("The default decision client is already registered with a different provider.", exception.Message);
    }

    [Fact]
    public void SystemOne_builder_exposes_its_HttpClient_builder()
    {
        var services = new ServiceCollection();
        var builder = services.AddSystemOneClient(o => o.ApiKey = "k");

        Assert.IsType<SystemOneClientServiceBuilder>(builder);
        Assert.Equal("Minos.NET", builder.HttpClient.Name);
    }
}
