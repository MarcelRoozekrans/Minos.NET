using Minos.SystemOne;

namespace Minos.Docs.Tests;

/// <summary>Sets the API key environment variables for a test, and restores them afterwards.</summary>
internal sealed class KeyEnvironment : IDisposable
{
    private readonly string? _typeSafe = Environment.GetEnvironmentVariable(SystemOneDefaults.ApiKeyEnvironmentVariable);
    private readonly string? _openRouter = Environment.GetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable);
    private readonly string? _baseAddress = Environment.GetEnvironmentVariable(SystemOneDefaults.BaseAddressEnvironmentVariable);

    public KeyEnvironment(string? typeSafe, string? openRouter) => Set(typeSafe, openRouter);

    public static void Set(string? typeSafe, string? openRouter)
    {
        Environment.SetEnvironmentVariable(SystemOneDefaults.ApiKeyEnvironmentVariable, typeSafe);
        Environment.SetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, openRouter);
        Environment.SetEnvironmentVariable(SystemOneDefaults.BaseAddressEnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SystemOneDefaults.ApiKeyEnvironmentVariable, _typeSafe);
        Environment.SetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, _openRouter);
        Environment.SetEnvironmentVariable(SystemOneDefaults.BaseAddressEnvironmentVariable, _baseAddress);
    }
}
