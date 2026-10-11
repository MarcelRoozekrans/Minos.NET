namespace Minos.SystemOne;

/// <summary>The telemetry names of the <c>/v1/systemone</c> protocol's providers.</summary>
internal static class SystemOneTelemetry
{
    /// <summary>The custom GenAI provider value for TypeSafe's API.</summary>
    public const string TypeSafe = "typesafe";

    /// <summary>The custom GenAI provider value for OpenRouter.</summary>
    public const string OpenRouter = "openrouter";

    /// <summary>The custom GenAI provider value for a vLLM Semantic Router.</summary>
    public const string VllmSemanticRouter = "vllm-sr";

    /// <summary>The custom GenAI provider value for any other <c>/v1/systemone</c> server.</summary>
    public const string Custom = "custom";

    /// <summary>Gets the GenAI provider value for <paramref name="provider"/>.</summary>
    public static string ProviderOf(SystemOneProvider provider) => provider switch
    {
        SystemOneProvider.OpenRouter => OpenRouter,
        SystemOneProvider.VllmSemanticRouter => VllmSemanticRouter,
        SystemOneProvider.Custom => Custom,
        _ => TypeSafe,
    };
}
