using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using static ZeroAlloc.Jev.DependencyInjection.Tests.Registrations;

namespace ZeroAlloc.Jev.DependencyInjection.Tests;

/// <summary>The default client's <c>AddJevClient</c> over a real <see cref="ServiceCollection"/>, its primary handler a stub.</summary>
public sealed class AddJevClientTests
{
    [Fact]
    public void DefaultClient_IsOneSingleton()
    {
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/"));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IJevClient>();

        Assert.IsType<JevClient>(client);
        Assert.Same(client, provider.GetRequiredService<IJevClient>());
        using var scope = provider.CreateScope();
        Assert.Same(client, scope.ServiceProvider.GetRequiredService<IJevClient>());
    }

    [Fact]
    public void FactoryHttpClient_GetsTheBaseAddressTimeoutAndUserAgent()
    {
        var services = new ServiceCollection();
        services.AddJevClient(options =>
        {
            options.BaseAddress = new Uri("http://proxy.local/jev");
            options.Timeout = TimeSpan.FromSeconds(12);
        });
        using var provider = services.BuildServiceProvider();

        // No API key: the HttpClient is configured without one. A fresh CreateClient builds exactly as the factory built
        // the resolved client's HttpClient, from the same named configuration, so this checks what the client received.
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ZeroAlloc.Jev");

        Assert.Equal(new Uri("http://proxy.local/jev/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(12), http.Timeout);
        Assert.Matches(UserAgentPattern, http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public async Task WithALoggingProvider_TheClientLogsTheEvaluation()
    {
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddFakeLogging());
        services.AddJevClient(Options("http://default.local/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        var records = provider.GetFakeLogCollector().GetSnapshot();
        Assert.Contains(records, record => record.Id.Id == 1001 && string.Equals(record.Category, "ZeroAlloc.Jev.JevClient", StringComparison.Ordinal));

        // The factory's own request logs are off for Jev's clients: they allocate on every call, logging or not.
        Assert.DoesNotContain(records, record => record.Category?.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task FactoryRequestLogs_ComeBackThroughTheBuilder()
    {
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddFakeLogging());
        services.AddJevClient(Options("http://default.local/")).ConfigurePrimaryHttpMessageHandler(() => handler).AddDefaultLogger();
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        Assert.Contains(
            provider.GetFakeLogCollector().GetSnapshot(),
            record => string.Equals(record.Category, "System.Net.Http.HttpClient.ZeroAlloc.Jev.ClientHandler", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutALoggingProvider_TheClientResolvesAndEvaluates()
    {
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        Assert.True(result.IsSuccess);

        // With every level disabled for the client's category, the client takes its unlogged path and logs nothing.
        Assert.False(provider.GetRequiredService<ILoggerFactory>().CreateLogger("ZeroAlloc.Jev.JevClient").IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public async Task HandlerAddedThroughTheBuilder_SeesTheClientsRequests()
    {
        var handler = Noul();
        var seen = new List<Uri?>();
        var services = new ServiceCollection();
        services
            .AddJevClient(Options("http://default.local/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddHttpMessageHandler(() => new RecordingHandler(seen));
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        Assert.Equal([new Uri("http://default.local/v1/systemone")], seen);
    }

    [Fact]
    public async Task RepeatCalls_StackTheirConfiguration_AndRegisterOneClient()
    {
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://first.local/", apiKey: "first-key")).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddJevClient(options => options.ApiKey = "second-key");
        services.AddJevClient();
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(IJevClient)).ToArray();
        Assert.True(registrations.Length == 1, $"Expected one IJevClient registration, found {registrations.Length}.");

        // The first call's primary handler and base address, the second call's key, and one User-Agent.
        var request = OnlyRequest(handler);
        Assert.Equal(new Uri("http://first.local/v1/systemone"), request.Uri);
        Assert.Equal("Bearer second-key", request.Authorization);
        Assert.Matches(UserAgentPattern, request.UserAgent);
    }

    [Fact]
    public void NullArguments_Throw_AndRegisterNothing()
    {
        IServiceCollection? none = null;
        var services = new ServiceCollection();

        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddJevClient()).ParamName);
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => none!.AddJevClient(_ => { })).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => services.AddJevClient((Action<JevClientOptions>)null!)).ParamName);
        Assert.Empty(services);
    }

    [Fact]
    public async Task DisposingTheClient_LeavesTheFactorysHandlerUsable()
    {
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IJevClient>();
        _ = await client.EvaluateAsync(Request());

        // The spec's claim: the singleton borrows its HttpClient, so disposing the client leaves the handler alone.
        ((IDisposable)client).Dispose();

        Assert.False(handler.Disposed);
        using var fresh = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ZeroAlloc.Jev");
        using var response = await fresh.GetAsync(new Uri("v1/probe", UriKind.Relative));
        Assert.True(response.IsSuccessStatusCode);
        Assert.False(handler.Disposed);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DisposingTheProvider_DisposesTheClient()
    {
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/")).ConfigurePrimaryHttpMessageHandler(() => Noul());
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IJevClient>();
        _ = await client.EvaluateAsync(Request());

        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(Request()));
    }

    [Fact]
    public void HandlerLifetime_IsInfinite_SoTheFactoryNeverRotatesTheHandler()
    {
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/"));
        using var provider = services.BuildServiceProvider();

        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get("ZeroAlloc.Jev");

        Assert.Equal(Timeout.InfiniteTimeSpan, factoryOptions.HandlerLifetime);
    }

    [Fact]
    public void PrimaryHandler_IsASocketsHttpHandlerThatRecyclesConnections_WithNoLoggingHandlerInFront()
    {
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/"));
        using var provider = services.BuildServiceProvider();

        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("ZeroAlloc.Jev"));

        var primary = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.Equal(TimeSpan.FromMinutes(2), primary.PooledConnectionLifetime);
        Assert.DoesNotContain(chain, handler => handler.GetType().Name.Contains("Logging", StringComparison.Ordinal));
    }

    [Fact]
    public void AClientTheAppRegisteredFirst_DoesNotStopTheFactoryClientBeingConfigured()
    {
        using var own = new JevClient(new JevClientOptions { ApiKey = "own-key" });
        var services = new ServiceCollection();
        services.AddSingleton<IJevClient>(own);
        services.AddJevClient(Options("http://default.local/"));
        using var provider = services.BuildServiceProvider();

        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ZeroAlloc.Jev");

        Assert.Equal(new Uri("http://default.local/"), http.BaseAddress);
        Assert.Same(own, provider.GetRequiredService<IJevClient>());
    }

    [Fact]
    public void NonPositiveTimeout_Throws_OnTheFirstResolve()
    {
        foreach (var timeout in new[] { TimeSpan.FromSeconds(-1), TimeSpan.Zero })
        {
            var services = new ServiceCollection();
            services.AddJevClient(options =>
            {
                Options("http://default.local/")(options);
                options.Timeout = timeout;
            });
            using var provider = services.BuildServiceProvider();

            var exception = Assert.Throws<ArgumentException>(() => provider.GetRequiredService<IJevClient>());

            Assert.StartsWith("The time-out must be positive.", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BaseAddressWithoutATrailingSlash_Throws_OnTheFirstResolve()
    {
        // An options base address gets its trailing slash; one the caller sets on the HttpClient through the builder does not.
        var services = new ServiceCollection();
        services
            .AddJevClient(Options("http://default.local/"))
            .ConfigureHttpClient(http => http.BaseAddress = new Uri("http://proxy.local/jev"));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<ArgumentException>(() => provider.GetRequiredService<IJevClient>());

        Assert.StartsWith("httpClient.BaseAddress must end with '/'.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Records each request's URI, then passes it on.</summary>
    private sealed class RecordingHandler(List<Uri?> seen) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen.Add(request.RequestUri);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
