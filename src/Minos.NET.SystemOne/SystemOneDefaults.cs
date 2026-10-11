namespace Minos.SystemOne;

/// <summary>Default values shared by the Minos client, matching TypeSafe's official SDKs.</summary>
public static class SystemOneDefaults
{
    /// <summary>Environment variable the client reads a TypeSafe API key from.</summary>
    public const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";

    /// <summary>Environment variable the client reads an OpenRouter API key from.</summary>
    public const string OpenRouterApiKeyEnvironmentVariable = "OPENROUTER_API_KEY";

    /// <summary>Environment variable that overrides the base address, as in the official SDKs.</summary>
    public const string BaseAddressEnvironmentVariable = "TYPESAFE_BASE_URL";

    /// <summary>Environment variable the client reads a Cloudflare API token from, for <see cref="SystemOneProvider.CloudflareWorkersAi"/>.</summary>
    public const string CloudflareApiTokenEnvironmentVariable = "CLOUDFLARE_API_TOKEN";

    /// <summary>Environment variable the client reads a Cloudflare account id from, for <see cref="SystemOneProvider.CloudflareWorkersAi"/>.</summary>
    public const string CloudflareAccountIdEnvironmentVariable = "CLOUDFLARE_ACCOUNT_ID";

    /// <summary>Gets the model alias used when a request does not name one.</summary>
    /// <remarks>A property rather than a constant, so a changed default is not compiled into callers.</remarks>
    public static string Model => "jev-latest";

    /// <summary>Gets the root address of TypeSafe's HTTP API.</summary>
    public static Uri TypeSafeBaseAddress { get; } = new("https://api.typesafe.ai/");

    /// <summary>Gets the root address of OpenRouter's System One API; requests go to <c>v1/systemone</c> under it.</summary>
    public static Uri OpenRouterBaseAddress { get; } = new("https://openrouter.ai/api/");

    /// <summary>
    /// Gets the root address of Cloudflare's API. The client appends <c>accounts/{AccountId}/ai/run/@cf/cloudflare/</c>, and
    /// requests go to the model under that.
    /// </summary>
    public static Uri CloudflareBaseAddress { get; } = new("https://api.cloudflare.com/client/v4/");
}
