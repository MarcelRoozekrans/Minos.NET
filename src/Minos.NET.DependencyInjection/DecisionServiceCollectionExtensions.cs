using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minos;
using Minos.DependencyInjection;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IDecisionClient"/> in an <see cref="IServiceCollection"/>, over <see cref="IHttpClientFactory"/>, with options from delegates or bound from <see cref="IConfiguration"/>.</summary>
/// <remarks>
/// Each registration is one <see cref="DecisionClient"/> singleton over its own named <see cref="HttpClient"/>, which
/// <see cref="DecisionClient.ConfigureHttpClient(HttpClient, DecisionClientOptions)"/> configures from the registration's named
/// <see cref="DecisionClientOptions"/>. The client reads those options once, when the container first builds it, and logs
/// through the container's <see cref="ILoggerFactory"/>. Its spans and metrics come from the <c>Minos</c> source
/// and meter. Invalid options fail a generic host at startup, through <see cref="DecisionClientOptions.Validate()"/> and
/// <c>ValidateOnStart</c>. Without a host they throw when the client is first resolved. Either way a value that fails
/// validation throws an <see cref="OptionsValidationException"/> that carries the core's message, and a configuration
/// value the binder cannot convert fails at the same point with the binder's <see cref="InvalidOperationException"/>.
/// The registration's named options are validated whenever they are first read, so creating its named
/// <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/> directly also needs valid options, an API key
/// included. The options are validated even when the app registers its own <see cref="IDecisionClient"/> first, so a test
/// host that replaces the client still needs an API key, such as a placeholder one.
/// </remarks>
public static class DecisionServiceCollectionExtensions
{
    // The default client's HttpClient name; a keyed client's is this, a colon and its key.
    private const string HttpClientName = "Minos.NET";

    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/>, configured from the default named <see cref="DecisionClientOptions"/>,
    /// defaults and environment variables.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddDecisionClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddDefault(services, configure: null, configuration: null);
    }

    /// <summary>Registers the default <see cref="IDecisionClient"/>, configured by <paramref name="configure"/>.</summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">
    /// Configures the default named <see cref="DecisionClientOptions"/>. Each call adds its delegate, and they run in order.
    /// </param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddDecisionClient(this IServiceCollection services, Action<DecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return AddDefault(services, configure, configuration: null);
    }

    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/>, its <see cref="DecisionClientOptions"/> bound from
    /// <paramref name="configuration"/>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">
    /// The configuration to bind, typically a section such as <c>builder.Configuration.GetSection("Minos")</c>. Its keys are
    /// the <see cref="DecisionClientOptions"/> property names. A configure delegate registered later for the default client
    /// overrides bound values. Changes after the client is built are not picked up.
    /// </param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddDecisionClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return AddDefault(services, configure: null, configuration);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, configured from the
    /// <see cref="DecisionClientOptions"/> named <paramref name="name"/>, defaults and environment variables. Inject it with
    /// <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static IHttpClientBuilder AddDecisionClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return AddKeyed(services, name, configure: null, configuration: null);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, configured by <paramref name="configure"/>.
    /// Inject it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <param name="configure">
    /// Configures the <see cref="DecisionClientOptions"/> named <paramref name="name"/>. Each call adds its delegate, and they
    /// run in order.
    /// </param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static IHttpClientBuilder AddDecisionClient(this IServiceCollection services, string name, Action<DecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddKeyed(services, name, configure, configuration: null);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, its <see cref="DecisionClientOptions"/> bound
    /// from <paramref name="configuration"/>. Inject it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <param name="configuration">
    /// The configuration to bind, typically a section such as <c>builder.Configuration.GetSection("Minos:OpenRouter")</c>.
    /// Its keys are the <see cref="DecisionClientOptions"/> property names. A configure delegate registered later for the same
    /// name overrides bound values. Changes after the client is built are not picked up.
    /// </param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static IHttpClientBuilder AddDecisionClient(this IServiceCollection services, string name, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configuration);
        return AddKeyed(services, name, configure: null, configuration);
    }

    private static IHttpClientBuilder AddDefault(
        IServiceCollection services, Action<DecisionClientOptions>? configure, IConfiguration? configuration)
    {
        var builder = AddOptionsAndHttpClient(services, OptionsDefaults.DefaultName, HttpClientName, configure, configuration);
        services.TryAddSingleton<IDecisionClient>(provider => Create(provider, OptionsDefaults.DefaultName, HttpClientName));
        return builder;
    }

    private static IHttpClientBuilder AddKeyed(
        IServiceCollection services, string name, Action<DecisionClientOptions>? configure, IConfiguration? configuration)
    {
        var httpClientName = HttpClientName + ":" + name;
        var builder = AddOptionsAndHttpClient(services, name, httpClientName, configure, configuration);
        services.TryAddKeyedSingleton<IDecisionClient>(name, (provider, _) => Create(provider, name, httpClientName));
        return builder;
    }

    // Adds the options and configures the HttpClient. The HttpClient name maps one-to-one to the options name, so the
    // first registration of either is the first of both. That first registration alone also:
    // - adds the options' validator and ValidateOnStart, so repeat calls do not stack validators. TryAddEnumerable
    //   cannot do this: it compares implementation types, so it would keep one validator for every name.
    // - configures the HttpClient, so a repeat call cannot replace a primary handler the caller set through the first
    //   call's builder. The factory's own request logging is removed: its handlers allocate on every request even when
    //   nothing logs, and the client logs each operation and each retried attempt itself. AddDefaultLogger on the
    //   returned builder brings it back.
    private static IHttpClientBuilder AddOptionsAndHttpClient(
        IServiceCollection services,
        string optionsName,
        string httpClientName,
        Action<DecisionClientOptions>? configure,
        IConfiguration? configuration)
    {
        var options = services.AddOptions<DecisionClientOptions>(optionsName);

        // Source-generated through EnableConfigurationBindingGenerator, so this Bind uses no reflection.
        if (configuration is not null)
        {
            options.Bind(configuration);
        }

        if (configure is not null)
        {
            options.Configure(configure);
        }

        var repeat = IsHttpClientConfigured(services, httpClientName);
        var builder = services.AddHttpClient(httpClientName);
        if (!repeat)
        {
            services.AddKeyedSingleton(httpClientName, new HttpClientConfigured());
            services.AddSingleton<IValidateOptions<DecisionClientOptions>>(new DecisionClientOptionsValidator(optionsName));
            options.ValidateOnStart();
            builder
                .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                .ConfigureHttpClient((provider, httpClient) => DecisionClient.ConfigureHttpClient(httpClient, OptionsOf(provider, optionsName)))
                .RemoveAllLoggers();
        }

        return builder;
    }

    // Whether AddDecisionClient has already configured the HttpClient httpClientName. It looks for the marker only this class
    // adds, so an IDecisionClient the app registered itself does not count.
    private static bool IsHttpClientConfigured(IServiceCollection services, string httpClientName)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(HttpClientConfigured) && Equals(descriptor.ServiceKey, httpClientName))
            {
                return true;
            }
        }

        return false;
    }

    private static DecisionClient Create(IServiceProvider provider, string optionsName, string httpClientName)
        => new(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName),
            OptionsOf(provider, optionsName),
            provider.GetService<ILoggerFactory>());

    private static DecisionClientOptions OptionsOf(IServiceProvider provider, string optionsName)
        => provider.GetRequiredService<IOptionsMonitor<DecisionClientOptions>>().Get(optionsName);

    // Registered under an HttpClient's name once AddDecisionClient has configured it; a repeat call finds it and skips that.
    private sealed class HttpClientConfigured;
}
