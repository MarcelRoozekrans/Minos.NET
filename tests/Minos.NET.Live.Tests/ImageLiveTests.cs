using Minos.SystemOne;
using Xunit.Abstractions;

namespace Minos.Live.Tests;

public sealed class ImageLiveTests
{
    private readonly ITestOutputHelper _output;

    public ImageLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact(SystemOneProvider.OpenRouter)]
    public async Task Perplexity_ReadsARedImage()
    {
        using var client = new SystemOneClient(new SystemOneClientOptions
        {
            Provider = SystemOneProvider.OpenRouter,
            MaxRetries = 0,
            Model = "perplexity/pplx-decider-v1.1-27b",
            Capabilities = new() { SupportsImages = true },
        });
        var set = Live.Built(QuestionSet.CreateBuilder()
            .Noul("red", "Is the attached image red?", out var red)
            .Choice("color", "What color is the attached image?", out var color, o => o.Option("red").Option("green").Option("blue")));
        IReadOnlyList<DecisionImage> images = [DecisionImage.FromBytes(Live.RedPng(), "image/png")];

        var result = await client.EvaluateAsync(set, "A product photo.", images);

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Get(red).Probability > 0.5);
        Assert.Equal("red", result.Value.Get(color).Value);
    }
}
