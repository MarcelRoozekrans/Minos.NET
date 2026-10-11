using Minos.SystemOne;
using Xunit.Abstractions;

namespace Minos.Live.Tests;

public sealed class LunaLiveTests
{
    private readonly ITestOutputHelper _output;

    public LunaLiveTests(ITestOutputHelper output) => _output = output;

    /// <summary>Opt-in: runs only with <c>MINOS_LIVE_LUNA=1</c>, for an account with zero data retention enabled.</summary>
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
