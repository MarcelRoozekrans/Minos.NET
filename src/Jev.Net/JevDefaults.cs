namespace Jev.Net;

/// <summary>
/// Default values shared by the Jev client, matching TypeSafe's official SDKs.
/// </summary>
public static class JevDefaults
{
    /// <summary>Environment variable the client reads the API key from.</summary>
    public const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";

    /// <summary>Model alias used when a request does not name one.</summary>
    public const string Model = "jev-latest";

    /// <summary>Root address of the TypeSafe HTTP API.</summary>
    public static Uri BaseAddress { get; } = new("https://api.typesafe.ai/");
}
