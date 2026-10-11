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

    [Theory]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData("x", true)]
    [InlineData("x", false)]
    public void Mixing_a_factory_and_a_SystemOne_registration_under_one_name_throws_and_registers_nothing(string? name, bool factoryFirst)
    {
        var services = new ServiceCollection();
        Action factory = name is null
            ? () => services.AddDecisionClient(_ => new FakeDecisionClient())
            : () => services.AddDecisionClient(name, _ => new FakeDecisionClient());
        Action systemOne = name is null
            ? () => services.AddSystemOneClient(o => o.ApiKey = "k")
            : () => services.AddSystemOneClient(name, o => o.ApiKey = "k");
        (factoryFirst ? factory : systemOne)();
        var registered = services.Count;

        var exception = Assert.Throws<InvalidOperationException>(factoryFirst ? systemOne : factory);

        Assert.Equal(
            name is null
                ? "The default decision client is already registered with a different provider."
                : "The decision client 'x' is already registered with a different provider.",
            exception.Message);
        Assert.Equal(registered, services.Count);
    }

    [Fact]
    public void NullArguments_Throw_AndRegisterNothing()
    {
        IServiceCollection? none = null;
        var services = new ServiceCollection();
        Func<IServiceProvider, IDecisionClient> factory = _ => new FakeDecisionClient();

        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddDecisionClient(factory)).ParamName);
        Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient(null!)).ParamName);
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddDecisionClient("x", factory)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient(null!, factory)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddDecisionClient(string.Empty, factory)).ParamName);
        Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() => services.AddDecisionClient("x", null!)).ParamName);
        Assert.Empty(services);
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
