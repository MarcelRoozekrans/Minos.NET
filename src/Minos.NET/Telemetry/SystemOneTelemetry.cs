namespace Minos.SystemOne;

/// <summary>The telemetry names of the <c>/v1/systemone</c> protocol's providers.</summary>
internal static class SystemOneTelemetry
{
    /// <summary>The custom GenAI provider value for TypeSafe's API.</summary>
    public const string TypeSafe = "typesafe";

    /// <summary>The custom GenAI provider value for OpenRouter.</summary>
    public const string OpenRouter = "openrouter";

    /// <summary>Gets the GenAI provider value for <paramref name="provider"/>.</summary>
    public static string ProviderOf(DecisionProvider provider) => provider == DecisionProvider.OpenRouter ? OpenRouter : TypeSafe;
}
