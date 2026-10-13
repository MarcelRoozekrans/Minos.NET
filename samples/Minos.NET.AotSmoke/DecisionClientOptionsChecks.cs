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
}
