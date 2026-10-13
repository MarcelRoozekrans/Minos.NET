using Minos.SystemOne;

namespace Minos.Conformance.Tests;

/// <summary>
/// The TypeSafe and OpenRouter targets. The replay suite builds them with a stand-in key; the live record tests, which
/// link this file, build them with the key from the environment and no retries, so each probe bills at most once.
/// </summary>
internal static class SystemOneTargets
{
    public static ConformanceTarget TypeSafe(string? apiKey = null, string? model = null, int? maxRetries = null)
        => Create("typesafe", SystemOneProvider.TypeSafe, ModelRule.TypeSafe, apiKey, model, maxRetries);

    public static ConformanceTarget OpenRouter(string? apiKey = null, string? model = null, int? maxRetries = null)
        => Create("openrouter", SystemOneProvider.OpenRouter, ModelRule.OpenRouter, apiKey, model, maxRetries);

    private static ConformanceTarget Create(string name, SystemOneProvider provider, ModelRule rule, string? apiKey, string? model, int? maxRetries)
    {
        var requested = model ?? SystemOneDefaults.Model;
        return new ConformanceTarget(name, ConformanceWire.SystemOne, requested, rule, http =>
        {
            var options = new SystemOneDecisionClientOptions { Provider = provider, ApiKey = apiKey, Model = requested };
            if (maxRetries is { } retries)
            {
                options.MaxRetries = retries;
            }

            SystemOneDecisionClient.ConfigureHttpClient(http, options);
            return new SystemOneDecisionClient(http, options);
        });
    }
}
