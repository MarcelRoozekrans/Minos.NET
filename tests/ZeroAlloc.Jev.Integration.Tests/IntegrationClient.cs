namespace ZeroAlloc.Jev.Integration.Tests;

/// <summary>Builds a <see cref="JevClient"/> configured for the WireMock fixtures: a fixed key, no jitter and short timings.</summary>
internal static class IntegrationClient
{
    public static JevClient Create(
        Uri baseAddress,
        int maxRetries = 0,
        TimeSpan? timeout = null,
        TimeSpan? initialBackoff = null,
        JevProvider provider = JevProvider.TypeSafe)
        => new(new JevClientOptions
        {
            Provider = provider,
            ApiKey = "integration-key",
            BaseAddress = baseAddress,
            MaxRetries = maxRetries,
            InitialBackoff = initialBackoff ?? TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
            Jitter = false,
        });
}

/// <summary>Reads the fixture files linked from <c>tests/ZeroAlloc.Jev.Tests/Fixtures</c>.</summary>
internal static class Fixtures
{
    public static string Text(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>
    /// Builds, from public types only, the request that serializes to <c>request-noul.json</c>; see
    /// <c>RequestSerializationTests.Noul_WithCriteria_WritesTrueAndFalse</c> for the same fixture built the
    /// same way.
    /// </summary>
    public static SystemOneRequest NoulRequest()
        => new()
        {
            State = "Help! My payouts have been failing for 3 days.",
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                ["is_urgent"] = new NoulQuestion
                {
                    Instructions = "Does this convey urgency?",
                    Criteria = new NoulCriteria
                    {
                        WhenTrue = "Explicitly time-sensitive",
                        WhenFalse = "No urgency expressed",
                    },
                },
            },
        };
}
