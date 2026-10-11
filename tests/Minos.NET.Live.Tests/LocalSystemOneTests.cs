using Minos.SystemOne;
using Xunit.Abstractions;

namespace Minos.Live.Tests;

/// <summary>
/// Asks a local <c>/v1/systemone</c> server all three question kinds through the <see cref="SystemOneProvider.Custom"/>
/// preset, with no key. The <c>local-systemone</c> workflow starts the server and sets the two variables.
/// </summary>
public sealed class LocalSystemOneTests
{
    private const string State = "Help! My payouts have been failing for 3 days and nobody answers.";

    private readonly ITestOutputHelper _output;

    public LocalSystemOneTests(ITestOutputHelper output) => _output = output;

    [LocalSystemOneFact]
    public async Task TypedSet_AnswersNoulChoiceAndScore()
    {
        using var client = Client();

        var result = await client.EvaluateAsync<LiveTriage>(State);

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        var triage = result.Value;

        Assert.InRange(triage.IsUrgent.Probability, 0.0, 1.0);

        Assert.True(Enum.IsDefined(triage.Team.Value));
        Assert.Equal(3, triage.Team.Probabilities.Count);
        var sum = 0.0;
        foreach (var (_, probability) in triage.Team.Probabilities)
        {
            sum += probability;
        }

        // ollajev reports probabilities to 4 decimals, so three rounded values can sum to about 1.5e-4 off 1.
        Assert.InRange(sum, 1 - 1e-3, 1 + 1e-3);

        Assert.True(Enum.IsDefined(triage.Frustration.Value));
        Assert.InRange(triage.Frustration.Expected, 0.0, 2.0);
    }

    [LocalSystemOneFact]
    public async Task RawRequest_ReturnsThreeAnswers()
    {
        using var client = Client();

        var result = await client.EvaluateAsync(Live.Request(Model()));

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Answers.Count);
        Live.AssertAnsweredEveryQuestion(result.Value);
        Live.LogServedModel(_output, Model(), result.Value);
    }

    // Custom with no key sends no Authorization header. MaxRetries is 0, so a failure surfaces instead of being retried.
    private static SystemOneClient Client() => new(new SystemOneClientOptions
    {
        Provider = SystemOneProvider.Custom,
        BaseAddress = new Uri(Environment.GetEnvironmentVariable(LocalSystemOneFactAttribute.UrlVariable)!),
        Model = Model(),
        MaxRetries = 0,
    });

    private static string Model()
        => Environment.GetEnvironmentVariable(LocalSystemOneFactAttribute.ModelVariable) is { Length: > 0 } model
            ? model
            : throw new InvalidOperationException(
                LocalSystemOneFactAttribute.ModelVariable + " must name the model the local server serves.");
}
