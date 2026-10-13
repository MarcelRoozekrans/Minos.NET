using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minos;
using Minos.DependencyInjection;
using Minos.SystemOne;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers a <see cref="SystemOneDecisionClient"/> as an <see cref="IDecisionClient"/> in an <see cref="IServiceCollection"/>, over <see cref="IHttpClientFactory"/>, with options from delegates or bound from <see cref="IConfiguration"/>.</summary>
/// <remarks>
/// Each registration returns a <see cref="SystemOneDecisionClientServiceBuilder"/>, and stages added to it, also through repeat
/// calls, land on one pipeline around the registered client, built once when the container first resolves it.
/// Each registration is one <see cref="SystemOneDecisionClient"/> singleton over its own named <see cref="HttpClient"/>, which
/// <see cref="SystemOneDecisionClient.ConfigureHttpClient(HttpClient, SystemOneDecisionClientOptions)"/> configures from the registration's named
/// <see cref="SystemOneDecisionClientOptions"/>. The client reads those options once, when the container first builds it, and logs
/// through the container's <see cref="ILoggerFactory"/>. Its own retries wait on the container's <see cref="TimeProvider"/> when one is registered. Its spans and metrics come from the <c>Minos</c> source
/// and meter. Invalid options fail a generic host at startup, through <see cref="SystemOneDecisionClientOptions.Validate()"/> and
/// <c>ValidateOnStart</c>. Without a host they throw when the client is first resolved. Either way a value that fails
/// validation throws an <see cref="OptionsValidationException"/> that carries the core's message, and a configuration
/// value the binder cannot convert fails at the same point with the binder's <see cref="InvalidOperationException"/>.
/// The registration's named options are validated whenever they are first read, so creating its named
/// <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/> directly also needs valid options, an API key
/// included. The options are validated even when the app registers its own <see cref="IDecisionClient"/> first, so a test
/// host that replaces the client still needs an API key, such as a placeholder one. A name another provider's
/// registration already holds, such as an <c>AddDecisionClient</c> factory registration, cannot be registered here too.
/// </remarks>
public static class SystemOneServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/>, a <see cref="SystemOneDecisionClient"/> configured from the default named
    /// <see cref="SystemOneDecisionClientOptions"/>, defaults and environment variables.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <returns>The registration's pipeline builder; its <see cref="SystemOneDecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The default client is already registered with a different provider.</exception>
    public static SystemOneDecisionClientServiceBuilder AddSystemOneDecisionClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Add(services, name: null, configure: null, configuration: null);
    }

    /// <summary>Registers the default <see cref="IDecisionClient"/>, a <see cref="SystemOneDecisionClient"/> configured by <paramref name="configure"/>.</summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">
    /// Configures the default named <see cref="SystemOneDecisionClientOptions"/>. Each call adds its delegate, and they run in order.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="SystemOneDecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The default client is already registered with a different provider.</exception>
    public static SystemOneDecisionClientServiceBuilder AddSystemOneDecisionClient(this IServiceCollection services, Action<SystemOneDecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(services, name: null, configure, configuration: null);
    }

    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/>, a <see cref="SystemOneDecisionClient"/> with its
    /// <see cref="SystemOneDecisionClientOptions"/> bound from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">
    /// The configuration to bind, typically a section such as <c>builder.Configuration.GetSection("Minos")</c>. Its keys are
    /// the <see cref="SystemOneDecisionClientOptions"/> property names. A configure delegate registered later for the default client
    /// overrides bound values. Changes after the client is built are not picked up.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="SystemOneDecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The default client is already registered with a different provider.</exception>
    public static SystemOneDecisionClientServiceBuilder AddSystemOneDecisionClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return Add(services, name: null, configure: null, configuration);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, a <see cref="SystemOneDecisionClient"/> configured
    /// from the <see cref="SystemOneDecisionClientOptions"/> named <paramref name="name"/>, defaults and environment variables. Inject
    /// it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <returns>The registration's pipeline builder; its <see cref="SystemOneDecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The client <paramref name="name"/> is already registered with a different provider.</exception>
    public static SystemOneDecisionClientServiceBuilder AddSystemOneDecisionClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Add(services, name, configure: null, configuration: null);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, a <see cref="SystemOneDecisionClient"/> configured
    /// by <paramref name="configure"/>. Inject it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <param name="configure">
    /// Configures the <see cref="SystemOneDecisionClientOptions"/> named <paramref name="name"/>. Each call adds its delegate, and they
    /// run in order.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="SystemOneDecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The client <paramref name="name"/> is already registered with a different provider.</exception>
    public static SystemOneDecisionClientServiceBuilder AddSystemOneDecisionClient(this IServiceCollection services, string name, Action<SystemOneDecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(services, name, configure, configuration: null);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, a <see cref="SystemOneDecisionClient"/> with its
    /// <see cref="SystemOneDecisionClientOptions"/> bound from <paramref name="configuration"/>. Inject it with
    /// <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <param name="configuration">
    /// The configuration to bind, typically a section such as <c>builder.Configuration.GetSection("Minos:OpenRouter")</c>.
    /// Its keys are the <see cref="SystemOneDecisionClientOptions"/> property names. A configure delegate registered later for the same
    /// name overrides bound values. Changes after the client is built are not picked up.
    /// </param>
    /// <returns>The registration's pipeline builder; its <see cref="SystemOneDecisionClientServiceBuilder.HttpClient"/> property is the builder of the client's <see cref="System.Net.Http.HttpClient"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The client <paramref name="name"/> is already registered with a different provider.</exception>
    public static SystemOneDecisionClientServiceBuilder AddSystemOneDecisionClient(this IServiceCollection services, string name, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configuration);
        return Add(services, name, configure: null, configuration);
    }

    // Finds or adds the registration, then adds the options. The HttpClient name is the registration key, which maps
    // one-to-one to the options name, so the first registration of either is the first of both. That first registration
    // alone also:
    // - creates the registration's pipeline builder, which a repeat call returns so stages land on one pipeline.
    // - adds the options' validator and ValidateOnStart, so repeat calls do not stack validators. TryAddEnumerable
    //   cannot do this: it compares implementation types, so it would keep one validator for every name.
    // - configures the HttpClient, so a repeat call cannot replace a primary handler the caller set through the first
    //   call's builder. The factory's own request logging is removed: its handlers allocate on every request even when
    //   nothing logs, and the client logs each operation and each retried attempt itself. AddDefaultLogger on the
    //   builder's HttpClient brings it back.
    // Bind and Configure run on every call. The registration is looked up first, so a name another provider holds
    // throws before this call adds anything.
    private static SystemOneDecisionClientServiceBuilder Add(
        IServiceCollection services, string? name, Action<SystemOneDecisionClientOptions>? configure, IConfiguration? configuration)
    {
        var optionsName = name ?? OptionsDefaults.DefaultName;
        var httpClientName = DecisionClientRegistration.KeyFor(name);
        var builder = DecisionClientRegistration.AddOrGet(
            services,
            name,
            () => new SystemOneDecisionClientServiceBuilder(services.AddHttpClient(httpClientName), name, provider => Create(provider, optionsName, httpClientName)),
            out var created);

        var options = services.AddOptions<SystemOneDecisionClientOptions>(optionsName);

        // Source-generated through EnableConfigurationBindingGenerator, so this Bind uses no reflection.
        if (configuration is not null)
        {
            options.Bind(configuration);
        }

        if (configure is not null)
        {
            options.Configure(configure);
        }

        if (created)
        {
            services.AddSingleton<IValidateOptions<SystemOneDecisionClientOptions>>(new SystemOneDecisionClientOptionsValidator(optionsName));
            options.ValidateOnStart();
            builder.HttpClient
                .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                .ConfigureHttpClient((provider, client) => SystemOneDecisionClient.ConfigureHttpClient(client, OptionsOf(provider, optionsName)))
                .RemoveAllLoggers();
        }

        return builder;
    }

    private static SystemOneDecisionClient Create(IServiceProvider provider, string optionsName, string httpClientName)
        => new(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName),
            OptionsOf(provider, optionsName),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System);

    private static SystemOneDecisionClientOptions OptionsOf(IServiceProvider provider, string optionsName)
        => provider.GetRequiredService<IOptionsMonitor<SystemOneDecisionClientOptions>>().Get(optionsName);
}
