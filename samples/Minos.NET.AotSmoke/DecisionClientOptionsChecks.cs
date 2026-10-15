using System.Text.Json;
using Minos.SystemOne;

namespace Minos.AotSmoke;

/// <summary><see cref="SystemOneDecisionClientOptions.Validate"/> under Native AOT, the check the constructors run before building a client.</summary>
internal static class DecisionClientOptionsChecks
{
    [Covers("Minos.SystemOne.SystemOneDecisionClientOptions.Validate() -> void")]
    public static void ValidateAcceptsValidOptionsAndRejectsInvalidOnes()
    {
        Program.Check(
            !SmokeAssert.Throws<Exception>(() => new SystemOneDecisionClientOptions { ApiKey = "smoke-key" }.Validate()),
            "SystemOneDecisionClientOptions.Validate accepts valid options under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => new SystemOneDecisionClientOptions { ApiKey = "smoke-key", MaxRetries = 11 }.Validate()),
            "SystemOneDecisionClientOptions.Validate rejects MaxRetries above 10 under Native AOT");

        using (SmokeAssert.NoApiKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<InvalidOperationException>(() => new SystemOneDecisionClientOptions().Validate()),
                "SystemOneDecisionClientOptions.Validate rejects options with no key anywhere under Native AOT");
        }
    }

    [Covers("Minos.SystemOne.OpenRouterProviderPreferences.OpenRouterProviderPreferences() -> void")]
    public static async Task OpenRouterProviderPreferencesAreSentAndRefusedElsewhere()
    {
        var preferences = new OpenRouterProviderPreferences { ZeroDataRetention = true, DenyDataCollection = true };
        var handler = new CapturingHandler(Program.TriageResponse);
        using (var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") })
        using (var client = new SystemOneDecisionClient(
            http,
            new SystemOneDecisionClientOptions { ApiKey = "smoke-key", Provider = SystemOneProvider.OpenRouter, MaxRetries = 0, ProviderPreferences = preferences }))
        {
            _ = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);
        }

        using var body = handler.Body();
        var sent = body.RootElement.TryGetProperty("provider", out var provider)
            && provider.GetProperty("zdr").GetBoolean()
            && string.Equals(provider.GetProperty("data_collection").GetString(), "deny", StringComparison.Ordinal);
        Program.Check(sent, "OpenRouter provider preferences are sent as the provider object under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(
                () => new SystemOneDecisionClientOptions { ApiKey = "smoke-key", ProviderPreferences = preferences }.Validate()),
            "OpenRouter provider preferences are refused for TypeSafe under Native AOT");
    }
}
