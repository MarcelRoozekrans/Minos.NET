namespace Minos.Live.Tests;

/// <summary>
/// A fact that runs only when <c>MINOS_LIVE=1</c> is set and the provider's API key is set; the calls are real and
/// billed.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(DecisionProvider provider)
    {
        Provider = provider;
        var variable = provider == DecisionProvider.OpenRouter
            ? DecisionDefaults.OpenRouterApiKeyEnvironmentVariable
            : DecisionDefaults.ApiKeyEnvironmentVariable;
        var optedIn = string.Equals(Environment.GetEnvironmentVariable("MINOS_LIVE"), "1", StringComparison.Ordinal);
        var keySet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));

        Skip = (optedIn, keySet) switch
        {
            (false, false) => "Set MINOS_LIVE=1 and " + variable + " to run this live test; it calls the real API and is billed.",
            (false, true) => "Set MINOS_LIVE=1 to run this live test; it calls the real API and is billed.",
            (true, false) => "Set " + variable + " to run this live test; it calls the real API and is billed.",
            (true, true) => null,
        };
    }

    public DecisionProvider Provider { get; }
}
