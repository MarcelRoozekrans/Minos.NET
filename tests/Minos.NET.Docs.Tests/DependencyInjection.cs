namespace Minos.Docs.Tests;

#region DependencyInjection_Consumer
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minos;
using Minos.SystemOne;

// One yes/no question, so the example stays short.
[Questions]
public partial record InboxCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

// A class asks for IDecisionClient in its constructor, and the container supplies the shared client.
public sealed class InboxTriage(IDecisionClient client)
{
    public async Task<string> TriageAsync(string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<InboxCheck>(message, cancellationToken);
        if (result.IsFailure)
        {
            return $"The call failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent" : "can wait";
    }
}
#endregion

public static class DecisionRegistration
{
    #region DependencyInjection_Register
    // The key is read from configuration, such as user secrets, and never written into the code.
    public static SystemOneDecisionClientServiceBuilder AddDecision(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<InboxTriage>();
        return builder.Services.AddSystemOneDecisionClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
    }
    #endregion
}

#region DependencyInjection_KeyedConsumer
// A keyed client is asked for by its key.
public sealed class InboxRouter([FromKeyedServices("local")] IDecisionClient client)
{
    public async Task<string> TriageAsync(string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<InboxCheck>(message, cancellationToken);
        if (result.IsFailure)
        {
            return $"The call failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent, via the local server" : "can wait, via the local server";
    }
}
#endregion

public static class KeyedRegistration
{
    #region DependencyInjection_Keyed
    public static void AddKeyedClients(IHostApplicationBuilder builder)
    {
        builder.Services.AddSystemOneDecisionClient("typesafe", options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
        builder.Services.AddSystemOneDecisionClient("local", options =>
        {
            // A server you run: Custom has no default address or model, and sends no key unless ApiKey is set.
            options.Provider = SystemOneProvider.Custom;
            options.BaseAddress = new Uri("http://localhost:8080/");
            options.Model = "local-model";
        });
        builder.Services.AddSingleton<InboxRouter>();
    }
    #endregion

    #region DependencyInjection_FromEnvironment
    // With no options at all, the key comes from TYPESAFE_API_KEY, the base address from TYPESAFE_BASE_URL when set,
    // and everything else from the defaults.
    public static void AddFromEnvironment(IServiceCollection services)
    {
        services.AddSystemOneDecisionClient();
        services.AddSystemOneDecisionClient("backup");
    }
    #endregion

    #region DependencyInjection_Configuration
    // Each client reads its own section.
    public static void AddFromConfiguration(IHostApplicationBuilder builder)
    {
        builder.Services.AddSystemOneDecisionClient(builder.Configuration.GetSection("Minos"));
        builder.Services.AddSystemOneDecisionClient("openrouter", builder.Configuration.GetSection("OpenRouter"));
    }
    #endregion

    #region DependencyInjection_ConfigurationProviders
    // Provider, Path and Capabilities bind like any other option.
    public static void AddProvidersFromConfiguration(IHostApplicationBuilder builder)
    {
        builder.Services.AddSystemOneDecisionClient("local", builder.Configuration.GetSection("Local"));
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
            return $"Minos is misconfigured: {string.Join(' ', exception.Failures)}";
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
    // The HttpClient property of the builder AddSystemOneDecisionClient returns is the client's HttpClient builder, so handlers
    // are added the usual way.
    public static IHttpClientBuilder AddTracedDecision(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        return services
            .AddSystemOneDecisionClient(options => options.ApiKey = apiKey)
            .HttpClient.AddHttpMessageHandler<TraceHeaderHandler>();
    }

    // ConfigureHttpClientDefaults adds a handler to every HttpClient the factory makes, the Minos clients included.
    public static void AddTracedEverywhere(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        services.AddSystemOneDecisionClient(options => options.ApiKey = apiKey);
    }

    // To keep a defaults handler off the Minos client, clear the handlers of its builder. This also clears any you
    // added through that builder, so add those inside the delegate, after the Clear.
    public static IHttpClientBuilder AddDecisionWithoutDefaultHandlers(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        return services
            .AddSystemOneDecisionClient(options => options.ApiKey = apiKey)
            .HttpClient.ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
    }

    // When a handler of yours retries, such as a standard resilience handler, turn the client's own retries off,
    // so the two do not multiply.
    public static void AddWithOwnRetries(IServiceCollection services, string apiKey)
        => services.AddSystemOneDecisionClient(options =>
        {
            options.ApiKey = apiKey;
            options.MaxRetries = 0;
        });
}
#endregion

public static class NeutralRegistration
{
    #region DependencyInjection_Factory
    // AddDecisionClient takes a factory and knows no provider. Use it for a client of your own, or one from another
    // protocol package. It returns the neutral DecisionClientServiceBuilder: Use stages, but no HttpClient property.
    public static DecisionClientServiceBuilder AddOwnClient(IServiceCollection services)
        => services.AddDecisionClient(provider => new AlwaysUrgent());
    #endregion
}

public static class WithoutThePackage
{
    #region DependencyInjection_WithoutPackage
    // A named HttpClient of your own, set up the way AddSystemOneDecisionClient sets up its client.
    public static void AddMinosHttpClient(IServiceCollection services, SystemOneDecisionClientOptions options)
        => services.AddHttpClient("minos")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigureHttpClient(http => SystemOneDecisionClient.ConfigureHttpClient(http, options));

    public static SystemOneDecisionClient Create(IHttpClientFactory factory, SystemOneDecisionClientOptions options)
        => new(factory.CreateClient("minos"), options);
    #endregion
}
