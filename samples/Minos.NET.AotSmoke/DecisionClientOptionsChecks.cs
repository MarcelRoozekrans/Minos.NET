namespace Minos.AotSmoke;

/// <summary><see cref="DecisionClientOptions.Validate"/> under Native AOT, the check the constructors run before building a client.</summary>
internal static class DecisionClientOptionsChecks
{
    [Covers("Minos.DecisionClientOptions.Validate() -> void")]
    public static void ValidateAcceptsValidOptionsAndRejectsInvalidOnes()
    {
        Program.Check(
            !SmokeAssert.Throws<Exception>(() => new DecisionClientOptions { ApiKey = "smoke-key" }.Validate()),
            "DecisionClientOptions.Validate accepts valid options under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => new DecisionClientOptions { ApiKey = "smoke-key", MaxRetries = 11 }.Validate()),
            "DecisionClientOptions.Validate rejects MaxRetries above 10 under Native AOT");

        using (SmokeAssert.NoApiKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<InvalidOperationException>(() => new DecisionClientOptions().Validate()),
                "DecisionClientOptions.Validate rejects options with no key anywhere under Native AOT");
        }
    }
}
