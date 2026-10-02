namespace ZeroAlloc.Jev.Docs.Tests;

#region DependencyInjection_Consumer
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ZeroAlloc.Jev;

// One yes/no question, so the example stays short.
[JevQuestions]
public partial record InboxCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

// A class asks for IJevClient in its constructor, and the container supplies the shared client.
public sealed class InboxTriage(IJevClient jev)
{
    public async Task<string> TriageAsync(string message, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync<InboxCheck>(message, ct);
        if (result.IsFailure)
        {
            return $"Jev failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent" : "can wait";
    }
}
#endregion

public static class JevRegistration
{
    #region DependencyInjection_Register
    // The key is read from configuration, such as user secrets, and never written into the code.
    public static IHttpClientBuilder AddJev(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<InboxTriage>();
        return builder.Services.AddJevClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
    }
    #endregion
}

#region DependencyInjection_KeyedConsumer
// A keyed client is asked for by its key.
public sealed class InboxRouter([FromKeyedServices("openrouter")] IJevClient jev)
{
    public async Task<string> TriageAsync(string message, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync<InboxCheck>(message, ct);
        if (result.IsFailure)
        {
            return $"Jev failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent, via OpenRouter" : "can wait, via OpenRouter";
    }
}
#endregion

public static class KeyedRegistration
{
    #region DependencyInjection_Keyed
    public static void AddKeyedJev(IHostApplicationBuilder builder)
    {
        builder.Services.AddJevClient("typesafe", options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
        builder.Services.AddJevClient("openrouter", options =>
        {
            options.Provider = JevProvider.OpenRouter;
            options.ApiKey = builder.Configuration["OpenRouter:ApiKey"];
        });
        builder.Services.AddSingleton<InboxRouter>();
    }
    #endregion

    #region DependencyInjection_FromEnvironment
    // With no options at all, the key comes from TYPESAFE_API_KEY, the base address from TYPESAFE_BASE_URL when set,
    // and everything else from the defaults.
    public static void AddFromEnvironment(IServiceCollection services)
    {
        services.AddJevClient();
        services.AddJevClient("backup");
    }
    #endregion

    #region DependencyInjection_Configuration
    // Each client reads its own section.
    public static void AddFromConfiguration(IHostApplicationBuilder builder)
    {
        builder.Services.AddJevClient(builder.Configuration.GetSection("Jev"));
        builder.Services.AddJevClient("openrouter", builder.Configuration.GetSection("OpenRouter"));
    }
    #endregion

    #region DependencyInjection_Startup
    // Invalid options fail here, when the host starts, not on the first request.
    public static async Task<string> TryStartAsync(IHost host)
    {
        try
        {
            await host.StartAsync();
            return "started";
        }
        catch (OptionsValidationException exception)
        {
            return $"Jev is misconfigured: {string.Join(' ', exception.Failures)}";
        }
    }
    #endregion
}

#region DependencyInjection_Handlers
// A handler of your own sees every request the client sends, and every retry.
public sealed class TraceHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Trace", "inbox");
        return base.SendAsync(request, cancellationToken);
    }
}

public static class HandlerRegistration
{
    // AddJevClient returns the builder of the client's HttpClient, so handlers are added the usual way.
    public static IHttpClientBuilder AddTracedJev(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        return services
            .AddJevClient(options => options.ApiKey = apiKey)
            .AddHttpMessageHandler<TraceHeaderHandler>();
    }

    // ConfigureHttpClientDefaults adds a handler to every HttpClient the factory makes, Jev's included.
    public static void AddTracedEverywhere(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        services.AddJevClient(options => options.ApiKey = apiKey);
    }

    // To keep a defaults handler off Jev's client, clear the handlers of its builder. This also clears any you added
    // through that builder, so add those inside the delegate, after the Clear.
    public static IHttpClientBuilder AddJevWithoutDefaultHandlers(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        return services
            .AddJevClient(options => options.ApiKey = apiKey)
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
    }

    // When a handler of yours retries, such as a standard resilience handler, turn Jev's own retries off,
    // so the two do not multiply.
    public static void AddWithOwnRetries(IServiceCollection services, string apiKey)
        => services.AddJevClient(options =>
        {
            options.ApiKey = apiKey;
            options.MaxRetries = 0;
        });
}
#endregion

public static class WithoutThePackage
{
    #region DependencyInjection_WithoutPackage
    // A named HttpClient of your own, set up the way AddJevClient sets up its client.
    public static void AddJevHttpClient(IServiceCollection services, JevClientOptions options)
        => services.AddHttpClient("jev")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigureHttpClient(http => JevClient.ConfigureHttpClient(http, options));

    public static JevClient Create(IHttpClientFactory factory, JevClientOptions options)
        => new(factory.CreateClient("jev"), options);
    #endregion
}
