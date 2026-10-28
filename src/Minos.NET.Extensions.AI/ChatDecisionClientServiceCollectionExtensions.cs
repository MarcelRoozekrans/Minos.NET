using Microsoft.Extensions.AI;
using Minos;
using Minos.Extensions.AI;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers a <see cref="ChatDecisionClient"/> over the container's <see cref="IChatClient"/> as an <see cref="IDecisionClient"/>.</summary>
/// <remarks>
/// Register the chat client first, for example with <c>AddChatClient</c>. The decision client is created once, when the
/// container first resolves it, and the container keeps owning the chat client: disposing the decision client does not
/// dispose it. Stages go on the returned <see cref="DecisionClientServiceBuilder"/>, as for any
/// <c>AddDecisionClient</c> registration.
/// </remarks>
public static class ChatDecisionClientServiceCollectionExtensions
{
    /// <summary>Registers the default <see cref="IDecisionClient"/>, which asks the container's <see cref="IChatClient"/>, with the default options.</summary>
    /// <param name="services">The services to add to.</param>
    /// <returns>The registration's pipeline builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The default client is already registered with a different provider. Resolving the client throws the same
    /// exception when no <see cref="IChatClient"/> is registered.
    /// </exception>
    public static DecisionClientServiceBuilder AddChatDecisionClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddDecisionClient(provider => provider.GetRequiredService<IChatClient>().AsDecisionClient());
    }

    /// <summary>Registers the default <see cref="IDecisionClient"/>, which asks the container's <see cref="IChatClient"/>.</summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">Configures this registration's <see cref="ChatDecisionClientOptions"/>, a new instance for each registration.</param>
    /// <returns>The registration's pipeline builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The default client is already registered with a different provider. Resolving the client throws the same
    /// exception when no <see cref="IChatClient"/> is registered.
    /// </exception>
    public static DecisionClientServiceBuilder AddChatDecisionClient(this IServiceCollection services, Action<ChatDecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddDecisionClient(provider => provider.GetRequiredService<IChatClient>().AsDecisionClient(Configured(configure)));
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, which asks the <see cref="IChatClient"/>
    /// registered under the same key, for example with <c>AddKeyedChatClient</c>, with the default options.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The service key of both the chat client and the decision client.</param>
    /// <returns>The registration's pipeline builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The client <paramref name="name"/> is already registered with a different provider. Resolving the client throws the
    /// same exception when no <see cref="IChatClient"/> is registered under <paramref name="name"/>.
    /// </exception>
    public static DecisionClientServiceBuilder AddChatDecisionClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return services.AddDecisionClient(name, provider => provider.GetRequiredKeyedService<IChatClient>(name).AsDecisionClient());
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, which asks the <see cref="IChatClient"/>
    /// registered under the same key, for example with <c>AddKeyedChatClient</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The service key of both the chat client and the decision client.</param>
    /// <param name="configure">Configures this registration's <see cref="ChatDecisionClientOptions"/>, a new instance for each registration.</param>
    /// <returns>The registration's pipeline builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/>, <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The client <paramref name="name"/> is already registered with a different provider. Resolving the client throws the
    /// same exception when no <see cref="IChatClient"/> is registered under <paramref name="name"/>.
    /// </exception>
    public static DecisionClientServiceBuilder AddChatDecisionClient(this IServiceCollection services, string name, Action<ChatDecisionClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddDecisionClient(name, provider => provider.GetRequiredKeyedService<IChatClient>(name).AsDecisionClient(Configured(configure)));
    }

    private static ChatDecisionClientOptions Configured(Action<ChatDecisionClientOptions> configure)
    {
        var options = new ChatDecisionClientOptions();
        configure(options);
        return options;
    }
}
