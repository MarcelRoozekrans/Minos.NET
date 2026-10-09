namespace Minos.Docs.Tests;

/// <summary>Sets the API key environment variables for a test, and restores them afterwards.</summary>
internal sealed class KeyEnvironment : IDisposable
{
    private readonly string? _typeSafe = Environment.GetEnvironmentVariable(JevDefaults.ApiKeyEnvironmentVariable);
    private readonly string? _openRouter = Environment.GetEnvironmentVariable(JevDefaults.OpenRouterApiKeyEnvironmentVariable);
    private readonly string? _baseAddress = Environment.GetEnvironmentVariable(JevDefaults.BaseAddressEnvironmentVariable);

    public KeyEnvironment(string? typeSafe, string? openRouter) => Set(typeSafe, openRouter);

    public static void Set(string? typeSafe, string? openRouter)
    {
        Environment.SetEnvironmentVariable(JevDefaults.ApiKeyEnvironmentVariable, typeSafe);
        Environment.SetEnvironmentVariable(JevDefaults.OpenRouterApiKeyEnvironmentVariable, openRouter);
        Environment.SetEnvironmentVariable(JevDefaults.BaseAddressEnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(JevDefaults.ApiKeyEnvironmentVariable, _typeSafe);
        Environment.SetEnvironmentVariable(JevDefaults.OpenRouterApiKeyEnvironmentVariable, _openRouter);
        Environment.SetEnvironmentVariable(JevDefaults.BaseAddressEnvironmentVariable, _baseAddress);
    }
}
