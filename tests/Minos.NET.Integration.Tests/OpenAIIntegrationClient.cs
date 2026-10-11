using Minos.OpenAI;

namespace Minos.Integration.Tests;

/// <summary>
/// Builds an <see cref="OpenAIDecisionClient"/> for the WireMock server, through the public API: a fixed key, no jitter
/// and short backoffs, as <see cref="IntegrationClient"/> does for System One. <c>serverRoot</c> is the WireMock root; the
/// client's base address is the versioned root <c>v1/</c> under it, as in OpenAI's SDKs, so requests reach
/// <c>/v1/decisions</c>.
/// </summary>
internal static class OpenAIIntegrationClient
{
    public static OpenAIDecisionClient Create(Uri serverRoot, int maxRetries = 0)
        => new(new OpenAIDecisionClientOptions
        {
            ApiKey = "integration-key",
            BaseAddress = new Uri(serverRoot, "v1/"),
            MaxRetries = maxRetries,
            InitialBackoff = TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            Jitter = false,
        });
}
