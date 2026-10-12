using Microsoft.Extensions.Configuration;

namespace Minos.Samples;

/// <summary>
/// The configuration of a sample that runs against several providers: one section per provider under
/// <c>Minos:Providers</c>, and <c>Minos:Active</c> naming the one to use.
/// </summary>
public static class SampleProviders
{
    /// <summary>The section that holds one section per provider.</summary>
    public const string ProvidersKey = "Minos:Providers";

    /// <summary>The setting that names the provider to use.</summary>
    public const string ActiveKey = "Minos:Active";

    /// <summary>The names of the provider sections, in the order configuration sorts its keys.</summary>
    public static IReadOnlyList<string> Names(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [.. configuration.GetSection(ProvidersKey).GetChildren().Select(section => section.Key)];
    }

    /// <summary>The provider section that <c>Minos:Active</c> names, matched without regard to case.</summary>
    /// <exception cref="InvalidOperationException">
    /// No provider is active, or the active one has no section. The message names the configured providers.
    /// </exception>
    public static IConfigurationSection Active(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var names = Names(configuration);
        var configured = names.Count == 0 ? "none" : string.Join(", ", names);
        var active = configuration[ActiveKey];
        if (string.IsNullOrWhiteSpace(active))
        {
            throw new InvalidOperationException(
                "No provider is active. Set Minos:Active, or pass --provider, to one of the configured providers: " + configured + ".");
        }

        var name = names.FirstOrDefault(n => string.Equals(n, active, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Unknown provider " + active + ". The configured providers are: " + configured + ".");
        return configuration.GetSection(ProvidersKey).GetSection(name);
    }
}
