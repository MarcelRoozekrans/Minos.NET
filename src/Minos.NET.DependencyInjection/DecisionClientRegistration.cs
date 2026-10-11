using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Minos.DependencyInjection;

/// <summary>
/// Finds or adds one decision client registration: its pipeline builder, registered under the registration key, and
/// the <see cref="IDecisionClient"/> built from it. Shared with the protocol packages through InternalsVisibleTo, so
/// every registration kind keys, repeats and conflicts the same way.
/// </summary>
internal static class DecisionClientRegistration
{
    /// <summary>The default registration's key; a keyed registration's is this, a colon and its name.</summary>
    internal const string DefaultKey = "Minos.NET";

    /// <summary>Gets the registration key of the client named <paramref name="name"/>, or of the default client for <see langword="null"/>.</summary>
    /// <param name="name">The client's name, or <see langword="null"/> for the default client.</param>
    /// <returns><c>Minos.NET</c>, or <c>Minos.NET:</c> and the name. A protocol package names its HttpClient with it too.</returns>
    internal static string KeyFor(string? name) => name is null ? DefaultKey : DefaultKey + ":" + name;

    /// <summary>
    /// Returns the registration's existing builder, or creates one with <paramref name="create"/> and registers it and
    /// its client. The first registration wins: a repeat call returns the same builder, so stages land on one pipeline.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type of the calling registration kind.</typeparam>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">The client's name and service key, or <see langword="null"/> for the default client.</param>
    /// <param name="create">Creates the builder; called only when the registration does not exist yet.</param>
    /// <param name="created"><see langword="true"/> when this call created the registration.</param>
    /// <returns>The registration's builder.</returns>
    /// <exception cref="InvalidOperationException">
    /// The registration exists with a builder of another type, that is, another provider. A derived type counts as another.
    /// </exception>
    internal static TBuilder AddOrGet<TBuilder>(IServiceCollection services, string? name, Func<TBuilder> create, out bool created)
        where TBuilder : DecisionClientServiceBuilder
    {
        var key = KeyFor(name);
        var existing = Find(services, key);
        if (existing is not null)
        {
            // The exact type, not "is TBuilder": a protocol builder derives from DecisionClientServiceBuilder, so a factory
            // registration would otherwise take a protocol registration's builder as its own and drop its factory.
            if (existing is TBuilder same && existing.GetType() == typeof(TBuilder))
            {
                created = false;
                return same;
            }

            throw new InvalidOperationException(name is null
                ? "The default decision client is already registered with a different provider."
                : $"The decision client '{name}' is already registered with a different provider.");
        }

        var builder = create();
        services.AddKeyedSingleton<DecisionClientServiceBuilder>(key, builder);
        if (name is null)
        {
            services.TryAddSingleton<IDecisionClient>(provider => builder.Build(provider));
        }
        else
        {
            services.TryAddKeyedSingleton<IDecisionClient>(name, (provider, _) => builder.Build(provider));
        }

        created = true;
        return builder;
    }

    // The builder a registration already added under key, or null. It looks for the keyed instance only this class adds,
    // so an IDecisionClient the app registered itself does not count.
    private static DecisionClientServiceBuilder? Find(IServiceCollection services, string key)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(DecisionClientServiceBuilder)
                && Equals(descriptor.ServiceKey, key))
            {
                return descriptor.KeyedImplementationInstance as DecisionClientServiceBuilder;
            }
        }

        return null;
    }
}
