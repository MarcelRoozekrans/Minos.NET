namespace Minos.OpenAI;

/// <summary>Default values for <see cref="OpenAIDecisionClient"/>.</summary>
public static class OpenAIDefaults
{
    /// <summary>Environment variable the client reads an OpenAI API key from.</summary>
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    /// <summary>
    /// Environment variable that overrides the base address. As in OpenAI's official SDKs, it holds the versioned API
    /// root, such as <c>https://proxy.example/v1</c>.
    /// </summary>
    public const string BaseAddressEnvironmentVariable = "OPENAI_BASE_URL";

    /// <summary>Gets the model used when neither the options nor a request name one.</summary>
    /// <remarks>A property rather than a constant, so a changed default is not compiled into callers.</remarks>
    public static string Model => "gpt-6-luna";

    /// <summary>
    /// Gets the versioned root of OpenAI's HTTP API, <c>https://api.openai.com/v1/</c>, as in OpenAI's official SDKs;
    /// requests go to <c>decisions</c> under it.
    /// </summary>
    public static Uri BaseAddress { get; } = new("https://api.openai.com/v1/");
}
