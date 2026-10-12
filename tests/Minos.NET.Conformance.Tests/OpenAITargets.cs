using Minos.OpenAI;

namespace Minos.Conformance.Tests;

/// <summary>
/// The OpenAI target. It states no model rule: the guide's examples, which its success fixtures come from, name no model,
/// so R1 and R2 skip until a recording shows what OpenAI reports.
/// </summary>
internal static class OpenAITargets
{
    public static ConformanceTarget OpenAI(string? apiKey = null, int? maxRetries = null)
        => new("openai", ConformanceWire.Decisions, OpenAIDefaults.Model, modelRule: null, http =>
        {
            var options = new OpenAIDecisionClientOptions { ApiKey = apiKey, Model = OpenAIDefaults.Model };
            if (maxRetries is { } retries)
            {
                options.MaxRetries = retries;
            }

            OpenAIDecisionClient.ConfigureHttpClient(http, options);
            return new OpenAIDecisionClient(http, options);
        });
}
