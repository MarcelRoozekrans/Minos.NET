using Minos.SystemOne;

namespace Minos.Docs.Tests;

/// <summary>Sets the API key environment variables for a test, and restores them afterwards.</summary>
internal sealed class KeyEnvironment : IDisposable
{
    private readonly string? _typeSafe = Environment.GetEnvironmentVariable(SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable);
    private readonly string? _openRouter = Environment.GetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable);
    private readonly string? _baseAddress = Environment.GetEnvironmentVariable(SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable);

    public KeyEnvironment(string? typeSafe, string? openRouter) => Set(typeSafe, openRouter);

    public static void Set(string? typeSafe, string? openRouter)
    {
        Environment.SetEnvironmentVariable(SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable, typeSafe);
        Environment.SetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, openRouter);
        Environment.SetEnvironmentVariable(SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable, _typeSafe);
        Environment.SetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, _openRouter);
        Environment.SetEnvironmentVariable(SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable, _baseAddress);
    }
}
