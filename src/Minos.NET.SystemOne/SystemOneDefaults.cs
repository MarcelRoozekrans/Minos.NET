namespace Minos.SystemOne;

/// <summary>
/// Default values for <see cref="SystemOneDecisionClient"/>: TypeSafe's and OpenRouter's addresses and environment
/// variables, and the default model, matching TypeSafe's official SDKs.
/// </summary>
public static class SystemOneDefaults
{
    /// <summary>Environment variable the client reads a TypeSafe API key from.</summary>
    public const string TypeSafeApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";

    /// <summary>Environment variable the client reads an OpenRouter API key from.</summary>
    public const string OpenRouterApiKeyEnvironmentVariable = "OPENROUTER_API_KEY";

    /// <summary>Environment variable that overrides TypeSafe's base address; no other provider reads it.</summary>
    public const string TypeSafeBaseAddressEnvironmentVariable = "TYPESAFE_BASE_URL";

    /// <summary>Gets the model alias that the TypeSafe and OpenRouter presets use when no model is set.</summary>
    /// <remarks>A property rather than a constant, so a changed default is not compiled into callers.</remarks>
    public static string Model => "jev-latest";

    /// <summary>Gets the root address of TypeSafe's HTTP API.</summary>
    public static Uri TypeSafeBaseAddress { get; } = new("https://api.typesafe.ai/");

    /// <summary>Gets the root address of OpenRouter's System One API; requests go to <c>v1/systemone</c> under it.</summary>
    public static Uri OpenRouterBaseAddress { get; } = new("https://openrouter.ai/api/");
}
