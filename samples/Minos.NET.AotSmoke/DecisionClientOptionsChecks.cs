using Minos.SystemOne;

namespace Minos.AotSmoke;

/// <summary><see cref="SystemOneClientOptions.Validate"/> under Native AOT, the check the constructors run before building a client.</summary>
internal static class DecisionClientOptionsChecks
{
    [Covers("Minos.SystemOne.SystemOneClientOptions.Validate() -> void")]
    public static void ValidateAcceptsValidOptionsAndRejectsInvalidOnes()
    {
        Program.Check(
            !SmokeAssert.Throws<Exception>(() => new SystemOneClientOptions { ApiKey = "smoke-key" }.Validate()),
            "SystemOneClientOptions.Validate accepts valid options under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => new SystemOneClientOptions { ApiKey = "smoke-key", MaxRetries = 11 }.Validate()),
            "SystemOneClientOptions.Validate rejects MaxRetries above 10 under Native AOT");

        using (SmokeAssert.NoApiKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<InvalidOperationException>(() => new SystemOneClientOptions().Validate()),
                "SystemOneClientOptions.Validate rejects options with no key anywhere under Native AOT");
        }
    }
}
