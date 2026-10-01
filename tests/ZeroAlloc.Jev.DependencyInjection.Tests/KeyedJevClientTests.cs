using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using static ZeroAlloc.Jev.DependencyInjection.Tests.Registrations;

namespace ZeroAlloc.Jev.DependencyInjection.Tests;

/// <summary>Keyed clients from <c>AddJevClient(name, ...)</c>, each with its own options and <see cref="HttpClient"/>.</summary>
public sealed class KeyedJevClientTests
{
    [Fact]
    public void KeyedClient_IsOneSingleton_AndNotTheDefault()
    {
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/"));
        services.AddJevClient("openrouter", Options("http://keyed.local/"));
        using var provider = services.BuildServiceProvider();

        var keyed = provider.GetRequiredKeyedService<IJevClient>("openrouter");

        Assert.IsType<JevClient>(keyed);
        Assert.Same(keyed, provider.GetRequiredKeyedService<IJevClient>("openrouter"));
        Assert.NotSame(provider.GetRequiredService<IJevClient>(), keyed);
        Assert.Null(provider.GetKeyedService<IJevClient>("typesafe"));
    }

    [Fact]
    public async Task DefaultAndKeyedClients_KeepTheirOwnHttpClient()
    {
        var defaultHandler = Noul();
        var keyedHandler = Noul();
        var services = new ServiceCollection();
        services
            .AddJevClient(Options("http://default.local/", apiKey: "default-key"))
            .ConfigurePrimaryHttpMessageHandler(() => defaultHandler);
        services
            .AddJevClient("openrouter", Options("http://keyed.local/api/", apiKey: "keyed-key"))
            .ConfigurePrimaryHttpMessageHandler(() => keyedHandler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());
        _ = await provider.GetRequiredKeyedService<IJevClient>("openrouter").EvaluateAsync(Request());

        var defaultRequest = OnlyRequest(defaultHandler);
        Assert.Equal(new Uri("http://default.local/v1/systemone"), defaultRequest.Uri);
        Assert.Equal("Bearer default-key", defaultRequest.Authorization);
        var keyedRequest = OnlyRequest(keyedHandler);
        Assert.Equal(new Uri("http://keyed.local/api/v1/systemone"), keyedRequest.Uri);
        Assert.Equal("Bearer keyed-key", keyedRequest.Authorization);
    }

    [Fact]
    public async Task TwoKeyedClients_HaveTheirOwnOptionsAndHttpClients()
    {
        var typesafeHandler = Noul();
        var openRouterHandler = Noul();
        var services = new ServiceCollection();
        services
            .AddJevClient("typesafe", options =>
            {
                options.ApiKey = "typesafe-key";
                options.BaseAddress = new Uri("http://typesafe.local/");
                options.Model = "jev-typesafe";
                options.Timeout = TimeSpan.FromSeconds(7);
                options.MaxRetries = 0;
            })
            .ConfigurePrimaryHttpMessageHandler(() => typesafeHandler);
        services
            .AddJevClient("openrouter", options =>
            {
                options.Provider = JevProvider.OpenRouter;
                options.ApiKey = "openrouter-key";
                options.BaseAddress = new Uri("http://openrouter.local/api/");
                options.Model = "jev-openrouter";
                options.Timeout = TimeSpan.FromSeconds(9);
                options.MaxRetries = 0;
            })
            .ConfigurePrimaryHttpMessageHandler(() => openRouterHandler);
        using var provider = services.BuildServiceProvider();

        // A built set sends the options' model, so each request shows which options its client read.
        var set = JevQuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out var urgent).Build().Value;
        var typesafe = provider.GetRequiredKeyedService<IJevClient>("typesafe");
        var openRouter = provider.GetRequiredKeyedService<IJevClient>("openrouter");
        var typesafeResult = await typesafe.EvaluateAsync(set, "Help!");
        var openRouterResult = await openRouter.EvaluateAsync(set, "Help!");

        Assert.NotSame(typesafe, openRouter);
        Assert.Null(provider.GetService<IJevClient>());
        Assert.Equal(0.95, typesafeResult.Value.Get(urgent).Probability, 3);
        Assert.True(openRouterResult.IsSuccess);

        var typesafeRequest = OnlyRequest(typesafeHandler);
        Assert.Equal(new Uri("http://typesafe.local/v1/systemone"), typesafeRequest.Uri);
        Assert.Equal("Bearer typesafe-key", typesafeRequest.Authorization);
        Assert.Contains("\"model\":\"jev-typesafe\"", typesafeRequest.Body, StringComparison.Ordinal);
        Assert.Matches(UserAgentPattern, typesafeRequest.UserAgent);

        var openRouterRequest = OnlyRequest(openRouterHandler);
        Assert.Equal(new Uri("http://openrouter.local/api/v1/systemone"), openRouterRequest.Uri);
        Assert.Equal("Bearer openrouter-key", openRouterRequest.Authorization);
        Assert.Contains("\"model\":\"jev-openrouter\"", openRouterRequest.Body, StringComparison.Ordinal);
        Assert.Matches(UserAgentPattern, openRouterRequest.UserAgent);

        // Each name's options hold their own provider, which the wire cannot show: the base addresses are overridden.
        var monitor = provider.GetRequiredService<IOptionsMonitor<JevClientOptions>>();
        Assert.Equal(JevProvider.TypeSafe, monitor.Get("typesafe").Provider);
        Assert.Equal(JevProvider.OpenRouter, monitor.Get("openrouter").Provider);
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var typesafeHttp = factory.CreateClient("ZeroAlloc.Jev:typesafe");
        using var openRouterHttp = factory.CreateClient("ZeroAlloc.Jev:openrouter");
        Assert.Equal(TimeSpan.FromSeconds(7), typesafeHttp.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(9), openRouterHttp.Timeout);
    }

    [Fact]
    public void KeyedClient_HasAnInfiniteHandlerLifetime_AndAPooledPrimaryHandler()
    {
        var services = new ServiceCollection();
        services.AddJevClient("openrouter", Options("http://keyed.local/"));
        using var provider = services.BuildServiceProvider();

        var lifetime = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get("ZeroAlloc.Jev:openrouter").HandlerLifetime;
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("ZeroAlloc.Jev:openrouter"));

        Assert.Equal(Timeout.InfiniteTimeSpan, lifetime);
        var primary = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.Equal(TimeSpan.FromMinutes(2), primary.PooledConnectionLifetime);
    }

    [Fact]
    public async Task RepeatKeyedCalls_StackTheirConfiguration_AndRegisterOneClient()
    {
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddJevClient("openrouter", Options("http://first.local/", apiKey: "first-key")).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddJevClient("openrouter", options => options.ApiKey = "second-key");
        services.AddJevClient("openrouter");
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredKeyedService<IJevClient>("openrouter").EvaluateAsync(Request());

        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(IJevClient)).ToArray();
        Assert.True(registrations.Length == 1, $"Expected one IJevClient registration, found {registrations.Length}.");
        var request = OnlyRequest(handler);
        Assert.Equal(new Uri("http://first.local/v1/systemone"), request.Uri);
        Assert.Equal("Bearer second-key", request.Authorization);
        Assert.Matches(UserAgentPattern, request.UserAgent);
    }

    [Fact]
    public void NullOrEmptyArguments_Throw_AndRegisterNothing()
    {
        IServiceCollection? none = null;
        var services = new ServiceCollection();
        Action<JevClientOptions> configure = _ => { };

        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddJevClient("openrouter")).ParamName);
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddJevClient("openrouter", configure)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddJevClient((string)null!)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => services.AddJevClient(null!, configure)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddJevClient(string.Empty)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => services.AddJevClient(string.Empty, configure)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => services.AddJevClient("openrouter", null!)).ParamName);
        Assert.Empty(services);
    }
}
