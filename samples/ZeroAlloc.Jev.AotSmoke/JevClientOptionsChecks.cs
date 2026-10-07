namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><see cref="JevClientOptions.Validate"/> under Native AOT, the check the constructors run before building a client.</summary>
internal static class JevClientOptionsChecks
{
    [Covers("ZeroAlloc.Jev.JevClientOptions.Validate() -> void")]
    public static void ValidateAcceptsValidOptionsAndRejectsInvalidOnes()
    {
        Program.Check(
            !SmokeAssert.Throws<Exception>(() => new JevClientOptions { ApiKey = "smoke-key" }.Validate()),
            "JevClientOptions.Validate accepts valid options under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => new JevClientOptions { ApiKey = "smoke-key", MaxRetries = 11 }.Validate()),
            "JevClientOptions.Validate rejects MaxRetries above 10 under Native AOT");

        using (SmokeAssert.NoApiKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<InvalidOperationException>(() => new JevClientOptions().Validate()),
                "JevClientOptions.Validate rejects options with no key anywhere under Native AOT");
        }
    }
}
