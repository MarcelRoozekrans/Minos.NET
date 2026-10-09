using Xunit.Abstractions;

namespace Minos.Live.Tests;

public sealed class OpenRouterLiveTests
{
    private readonly ITestOutputHelper _output;

    public OpenRouterLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact(JevProvider.OpenRouter)]
    public async Task Evaluate_AnswersEveryQuestion()
    {
        using var client = Live.Client(JevProvider.OpenRouter);

        var result = await client.EvaluateAsync(Live.Request(JevProvider.OpenRouter));

        if (result.IsFailure)
        {
            // Logged before asserting so a model-id problem is visible even though the assertion below fails.
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Live.AssertAnsweredEveryQuestion(result.Value);
        Assert.NotNull(result.Value.Id);
        Assert.NotNull(result.Value.Provider);
        Assert.NotNull(result.Value.Usage.Cost);
    }
}
