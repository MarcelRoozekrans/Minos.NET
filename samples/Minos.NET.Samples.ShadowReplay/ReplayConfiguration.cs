using Microsoft.Extensions.Configuration;
using Minos.SystemOne;

namespace Minos.Samples.ShadowReplay;

/// <summary>Reads a provider's options from the <c>Minos:Providers</c> sections of a config file, as <c>minos calibrate</c> does.</summary>
public static class ReplayConfiguration
{
    private const string ProvidersKey = "Minos:Providers";

    /// <summary>Resolves the options of one provider section: the JSON file, with environment variables such as <c>Minos__Providers__Local__Model</c> layered on top.</summary>
    /// <param name="configPath">The JSON config file.</param>
    /// <param name="section">The provider section, matched without regard to case.</param>
    /// <returns>The validated options.</returns>
    /// <exception cref="ArgumentException">An argument is empty.</exception>
    /// <exception cref="InvalidOperationException">The section is missing, or its options are not valid.</exception>
    public static SystemOneDecisionClientOptions Resolve(string configPath, string section)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.GetFullPath(configPath), optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var names = configuration.GetSection(ProvidersKey).GetChildren().Select(s => s.Key).ToArray();
        var name = names.FirstOrDefault(n => string.Equals(n, section, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Unknown provider section '{section}'. The configured sections are: {(names.Length == 0 ? "none" : string.Join(", ", names))}.");

        var options = configuration.GetSection(ProvidersKey).GetSection(name).Get<SystemOneDecisionClientOptions>() ?? new SystemOneDecisionClientOptions();
        options.Validate();
        return options;
    }
}
