using Minos.SystemOne;
using Xunit.Abstractions;

namespace Minos.Live.Tests;

public sealed class LunaLiveTests
{
    private readonly ITestOutputHelper _output;

    public LunaLiveTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Opt-in: runs only with <c>MINOS_LIVE_LUNA=1</c>, for an OpenRouter account that allows endpoints which retain data;
    /// zero-data-retention-only accounts get 404 <c>zdr-violation-by-account</c>.
    /// </summary>
    [LiveFact(SystemOneProvider.OpenRouter, RequiresEnvironment = "MINOS_LIVE_LUNA")]
    public async Task Luna_AnswersText()
    {
        using var client = new SystemOneClient(new SystemOneClientOptions
        {
            Provider = SystemOneProvider.OpenRouter,
            MaxRetries = 0,
            Model = "openai/gpt-6-luna-decisions",
        });

        var result = await client.EvaluateAsync(Live.Request("openai/gpt-6-luna-decisions"));

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Live.AssertAnsweredEveryQuestion(result.Value);
    }
}
