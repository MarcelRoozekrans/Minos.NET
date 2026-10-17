using Microsoft.Extensions.Configuration;
using Minos.SystemOne;
using ZeroAlloc.Results;

namespace Minos.Tool;

/// <summary>Reads the <c>Minos</c> section of the configuration chain: the config file, then the environment.</summary>
internal static class ToolConfiguration
{
    private const string ProvidersKey = "Minos:Providers";
    private const string ActiveKey = "Minos:Active";

    /// <summary>The default config file, in the current directory.</summary>
    public const string DefaultFile = "minos.json";

    /// <summary>Resolves the provider's options.</summary>
    /// <param name="configPath">The JSON config file.</param>
    /// <param name="provider">The provider section to use, or <see langword="null"/> for <c>Minos:Active</c>.</param>
    /// <param name="model">A model that overrides the section's, or <see langword="null"/>.</param>
    /// <returns>The provider's section name and validated options, or the message to print.</returns>
    public static Result<(string Name, SystemOneDecisionClientOptions Options), string> Resolve(string configPath, string? provider, string? model)
    {
        if (!File.Exists(configPath))
        {
            return Fail("The config file '" + configPath + "' does not exist. Pass --config, or create minos.json in the current directory.");
        }

        IConfigurationRoot configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.GetFullPath(configPath), optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or FormatException)
        {
            return Fail("The config file '" + configPath + "' cannot be read: " + ex.Message);
        }

        var names = configuration.GetSection(ProvidersKey).GetChildren().Select(s => s.Key).ToArray();
        var configured = names.Length == 0 ? "none" : string.Join(", ", names);
        var wanted = string.IsNullOrWhiteSpace(provider) ? configuration[ActiveKey] : provider;
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return Fail("No provider is chosen. Pass --provider, or set Minos:Active, to one of the configured providers: " + configured + ".");
        }

        var name = names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return Fail("Unknown provider '" + wanted + "'. The configured providers are: " + configured + ".");
        }

        try
        {
            var options = configuration.GetSection(ProvidersKey).GetSection(name).Get<SystemOneDecisionClientOptions>() ?? new SystemOneDecisionClientOptions();
            if (!string.IsNullOrWhiteSpace(model))
            {
                options.Model = model;
            }

            options.Validate();
            return Result<(string Name, SystemOneDecisionClientOptions Options), string>.Success((name, options));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return Fail("The options of provider '" + name + "' are invalid: " + ex.Message);
        }
    }

    private static Result<(string Name, SystemOneDecisionClientOptions Options), string> Fail(string message)
        => Result<(string Name, SystemOneDecisionClientOptions Options), string>.Failure(message);
}
