using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minos.OpenAI;
using Minos.SystemOne;

namespace Minos.Docs.Tests;

public static class ProviderSwitching
{
    #region Providers_ActiveProvider
    // Minos:Active names one section under Minos:Providers, and the client is bound from that section alone. No other
    // code depends on the provider.
    public static IConfigurationSection ActiveSection(IConfiguration configuration)
    {
        var active = configuration["Minos:Active"];
        if (string.IsNullOrWhiteSpace(active))
        {
            throw new InvalidOperationException("Set Minos:Active to the provider to use.");
        }

        var section = configuration.GetSection("Minos:Providers").GetSection(active);
        return section.Exists()
            ? section
            : throw new InvalidOperationException($"Minos:Providers has no section named {active}.");
    }

    public static SystemOneDecisionClientServiceBuilder AddActiveProvider(IHostApplicationBuilder builder)
        => builder.Services.AddSystemOneDecisionClient(ActiveSection(builder.Configuration));
    #endregion

    #region Providers_AddOpenAI
    // OpenAI has a client type of its own, so its section is bound with AddOpenAIDecisionClient. The code that asks for
    // IDecisionClient does not change. Without an ApiKey in the section, the client reads OPENAI_API_KEY.
    public static void AddActiveClient(IHostApplicationBuilder builder)
    {
        var section = ActiveSection(builder.Configuration);
        if (string.Equals(section.Key, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddOpenAIDecisionClient(section);
        }
        else
        {
            builder.Services.AddSystemOneDecisionClient(section);
        }
    }
    #endregion
}
