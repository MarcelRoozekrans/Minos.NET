using Minos.SystemOne;
using Xunit.Abstractions;

namespace Minos.Live.Tests;

public sealed class ClefLiveTests
{
    private readonly ITestOutputHelper _output;

    public ClefLiveTests(ITestOutputHelper output) => _output = output;

    /// <summary>Text only: Clef answered images wrongly in the probes.</summary>
    [LiveFact(SystemOneProvider.OpenRouter)]
    public async Task ClefFlash_AnswersText()
    {
        using var client = new SystemOneDecisionClient(new SystemOneDecisionClientOptions
        {
            Provider = SystemOneProvider.OpenRouter,
            MaxRetries = 0,
            Model = "cloudflare/clef-flash",
        });

        var result = await client.EvaluateAsync(Live.Request("cloudflare/clef-flash"));

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Live.AssertAnsweredEveryQuestion(result.Value);
    }
}
