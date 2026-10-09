namespace Minos.Docs.Tests;

/// <summary>Sets the API key environment variables for a test, and restores them afterwards.</summary>
internal sealed class KeyEnvironment : IDisposable
{
    private readonly string? _typeSafe = Environment.GetEnvironmentVariable(DecisionDefaults.ApiKeyEnvironmentVariable);
    private readonly string? _openRouter = Environment.GetEnvironmentVariable(DecisionDefaults.OpenRouterApiKeyEnvironmentVariable);
    private readonly string? _baseAddress = Environment.GetEnvironmentVariable(DecisionDefaults.BaseAddressEnvironmentVariable);

    public KeyEnvironment(string? typeSafe, string? openRouter) => Set(typeSafe, openRouter);

    public static void Set(string? typeSafe, string? openRouter)
    {
        Environment.SetEnvironmentVariable(DecisionDefaults.ApiKeyEnvironmentVariable, typeSafe);
        Environment.SetEnvironmentVariable(DecisionDefaults.OpenRouterApiKeyEnvironmentVariable, openRouter);
        Environment.SetEnvironmentVariable(DecisionDefaults.BaseAddressEnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DecisionDefaults.ApiKeyEnvironmentVariable, _typeSafe);
        Environment.SetEnvironmentVariable(DecisionDefaults.OpenRouterApiKeyEnvironmentVariable, _openRouter);
        Environment.SetEnvironmentVariable(DecisionDefaults.BaseAddressEnvironmentVariable, _baseAddress);
    }
}
