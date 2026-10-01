using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroAlloc.Jev;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IJevClient"/> in an <see cref="IServiceCollection"/>, over <see cref="IHttpClientFactory"/>.</summary>
/// <remarks>
/// Each registration is one <see cref="JevClient"/> singleton over its own named <see cref="HttpClient"/>, which
/// <see cref="JevClient.ConfigureHttpClient(HttpClient, JevClientOptions)"/> configures from the registration's named
/// <see cref="JevClientOptions"/>. The client reads those options once, when the container first builds it, and logs
/// through the container's <see cref="ILoggerFactory"/>. Its spans and metrics come from the <c>ZeroAlloc.Jev</c> source
/// and meter. Invalid options throw when the client is first resolved.
/// </remarks>
public static class JevServiceCollectionExtensions
{
    // The default client's HttpClient name; a keyed client's is this, a colon and its key.
    private const string HttpClientName = "ZeroAlloc.Jev";

    /// <summary>
    /// Registers the default <see cref="IJevClient"/>, configured from the default named <see cref="JevClientOptions"/>,
    /// defaults and environment variables.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddJevClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddDefault(services, configure: null);
    }

    /// <summary>Registers the default <see cref="IJevClient"/>, configured by <paramref name="configure"/>.</summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">
    /// Configures the default named <see cref="JevClientOptions"/>. Each call adds its delegate, and they run in order.
    /// </param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddJevClient(this IServiceCollection services, Action<JevClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return AddDefault(services, configure);
    }

    /// <summary>
    /// Registers an <see cref="IJevClient"/> keyed by <paramref name="name"/>, configured from the
    /// <see cref="JevClientOptions"/> named <paramref name="name"/>, defaults and environment variables. Inject it with
    /// <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static IHttpClientBuilder AddJevClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return AddKeyed(services, name, configure: null);
    }

    /// <summary>
    /// Registers an <see cref="IJevClient"/> keyed by <paramref name="name"/>, configured by <paramref name="configure"/>.
    /// Inject it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key and options name.</param>
    /// <param name="configure">
    /// Configures the <see cref="JevClientOptions"/> named <paramref name="name"/>. Each call adds its delegate, and they
    /// run in order.
    /// </param>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static IHttpClientBuilder AddJevClient(this IServiceCollection services, string name, Action<JevClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddKeyed(services, name, configure);
    }

    private static IHttpClientBuilder AddDefault(IServiceCollection services, Action<JevClientOptions>? configure)
    {
        var builder = AddOptionsAndHttpClient(services, OptionsDefaults.DefaultName, HttpClientName, configure);
        services.TryAddSingleton<IJevClient>(provider => Create(provider, OptionsDefaults.DefaultName, HttpClientName));
        return builder;
    }

    private static IHttpClientBuilder AddKeyed(IServiceCollection services, string name, Action<JevClientOptions>? configure)
    {
        var httpClientName = HttpClientName + ":" + name;
        var builder = AddOptionsAndHttpClient(services, name, httpClientName, configure);
        services.TryAddKeyedSingleton<IJevClient>(name, (provider, _) => Create(provider, name, httpClientName));
        return builder;
    }

    // Configures the HttpClient on the first registration of its name only, so a repeat call cannot replace a primary
    // handler the caller set through the first call's builder. The factory's own request logging is removed: its
    // handlers allocate on every request even when nothing logs, and the client logs each operation and each retried
    // attempt itself. AddDefaultLogger on the returned builder brings it back.
    private static IHttpClientBuilder AddOptionsAndHttpClient(
        IServiceCollection services, string optionsName, string httpClientName, Action<JevClientOptions>? configure)
    {
        var options = services.AddOptions<JevClientOptions>(optionsName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        var repeat = IsHttpClientConfigured(services, httpClientName);
        var builder = services.AddHttpClient(httpClientName);
        if (!repeat)
        {
            services.AddKeyedSingleton(httpClientName, new HttpClientConfigured());
            builder
                .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                .ConfigureHttpClient((provider, httpClient) => JevClient.ConfigureHttpClient(httpClient, OptionsOf(provider, optionsName)))
                .RemoveAllLoggers();
        }

        return builder;
    }

    // Whether AddJevClient has already configured the HttpClient httpClientName. It looks for the marker only this class
    // adds, so an IJevClient the app registered itself does not count.
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

    private static JevClient Create(IServiceProvider provider, string optionsName, string httpClientName)
        => new(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName),
            OptionsOf(provider, optionsName),
            provider.GetService<ILoggerFactory>());

    private static JevClientOptions OptionsOf(IServiceProvider provider, string optionsName)
        => provider.GetRequiredService<IOptionsMonitor<JevClientOptions>>().Get(optionsName);

    // Registered under an HttpClient's name once AddJevClient has configured it; a repeat call finds it and skips that.
    private sealed class HttpClientConfigured;
}
