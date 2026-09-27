namespace ZeroAlloc.Jev.Live.Tests;

/// <summary>A fact that runs only when the provider's API key is set; the calls are real and may be billed.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(JevProvider provider)
    {
        Provider = provider;
        var variable = provider == JevProvider.OpenRouter
            ? JevDefaults.OpenRouterApiKeyEnvironmentVariable
            : JevDefaults.ApiKeyEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = "Set " + variable + " to run this live test; it calls the real API.";
        }
    }

    public JevProvider Provider { get; }
}
