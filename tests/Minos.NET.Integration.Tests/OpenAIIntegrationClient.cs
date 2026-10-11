using Minos.OpenAI;

namespace Minos.Integration.Tests;

/// <summary>
/// Builds an <see cref="OpenAIDecisionClient"/> for the WireMock server, through the public API: a fixed key, no jitter
/// and short backoffs, as <see cref="IntegrationClient"/> does for System One.
/// </summary>
internal static class OpenAIIntegrationClient
{
    public static OpenAIDecisionClient Create(Uri baseAddress, int maxRetries = 0)
        => new(new OpenAIDecisionClientOptions
        {
            ApiKey = "integration-key",
            BaseAddress = baseAddress,
            MaxRetries = maxRetries,
            InitialBackoff = TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            Jitter = false,
        });
}
