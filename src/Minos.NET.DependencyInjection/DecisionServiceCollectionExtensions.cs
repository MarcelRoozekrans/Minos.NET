using Minos;
using Minos.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IDecisionClient"/> in an <see cref="IServiceCollection"/>, as the default client or as keyed clients, over a client factory.</summary>
/// <remarks>
/// Each registration returns a <see cref="DecisionClientServiceBuilder"/>, and stages added to it, also through repeat
/// calls, land on one pipeline around the registered client, built once when the container first resolves it. The first
/// registration of a name wins: a repeat call returns the same builder and does not replace the factory. A protocol
/// package registers its own client the same way, through its own extension methods, such as <c>AddSystemOneDecisionClient</c>
/// in <c>Minos.NET.SystemOne</c>; one name holds one provider's registration. An <see cref="IDecisionClient"/> the app
/// registers itself first still wins.
/// </remarks>
public static class DecisionServiceCollectionExtensions
{
    /// <summary>Registers the default <see cref="IDecisionClient"/>, created by <paramref name="factory"/>.</summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="factory">
    /// Creates the client the registration's stages wrap, from the container's services, once, when the container first
    /// resolves the client.
    /// </param>
    /// <returns>The registration's pipeline builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The default client is already registered with a different provider.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services, Func<IServiceProvider, IDecisionClient> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        return DecisionClientRegistration.AddOrGet(services, name: null, () => new DecisionClientServiceBuilder(name: null, factory), out _);
    }

    /// <summary>
    /// Registers an <see cref="IDecisionClient"/> keyed by <paramref name="name"/>, created by <paramref name="factory"/>.
    /// Inject it with <c>[FromKeyedServices(name)]</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's service key.</param>
    /// <param name="factory">
    /// Creates the client the registration's stages wrap, from the container's services, once, when the container first
    /// resolves the client.
    /// </param>
    /// <returns>The registration's pipeline builder.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The client <paramref name="name"/> is already registered with a different provider.</exception>
    public static DecisionClientServiceBuilder AddDecisionClient(this IServiceCollection services, string name, Func<IServiceProvider, IDecisionClient> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        return DecisionClientRegistration.AddOrGet(services, name, () => new DecisionClientServiceBuilder(name, factory), out _);
    }
}
