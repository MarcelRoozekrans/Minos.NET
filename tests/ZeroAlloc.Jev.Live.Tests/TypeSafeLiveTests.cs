using Xunit.Abstractions;

namespace ZeroAlloc.Jev.Live.Tests;

public sealed class TypeSafeLiveTests
{
    private readonly ITestOutputHelper _output;

    public TypeSafeLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact(JevProvider.TypeSafe)]
    public async Task Evaluate_AnswersEveryQuestion()
    {
        using var client = Live.Client(JevProvider.TypeSafe);

        var result = await client.EvaluateAsync(Live.Request(JevProvider.TypeSafe));

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Live.AssertAnsweredEveryQuestion(result.Value);
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task ListModels_IncludesTheDefaultModel()
    {
        using var client = Live.Client(JevProvider.TypeSafe);

        var result = await client.ListModelsAsync();

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Models);
        _output.WriteLine("Models: " + string.Join(", ", result.Value.Models.Select(model => model.Name)));
        Assert.Contains(result.Value.Models, model => string.Equals(model.Name, JevDefaults.Model, StringComparison.Ordinal));
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task GeneratedQuestionSet_ParsesTypedAnswers()
    {
        using var client = Live.Client(JevProvider.TypeSafe);

        var result = await client.EvaluateAsync<LiveTriage>(
            "Help! My payouts have been failing for 3 days and nobody answers.");

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);

        var triage = result.Value;

        Assert.InRange(triage.IsUrgent.Probability, 0.0, 1.0);

        Assert.InRange(triage.Team.Confidence, 0.0, 1.0);
        Assert.True(Enum.IsDefined(triage.Team.Value));
        Assert.Equal(3, triage.Team.Probabilities.Count);
        Assert.InRange(triage.Team.Probabilities[triage.Team.Value], 0.0, 1.0);

        Assert.InRange(triage.Frustration.Confidence, 0.0, 1.0);
        Assert.True(Enum.IsDefined(triage.Frustration.Value));
        Assert.Equal(3, triage.Frustration.Probabilities.Count);
        Assert.InRange(triage.Frustration.Expected, 0.0, 2.0);
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task StructuredCriteria_ParsesTypedAnswers()
    {
        using var client = Live.Client(JevProvider.TypeSafe);

        var result = await client.EvaluateAsync<LiveStructuredRouting>(
            "Help! My payouts have been failing for 3 days and nobody answers.");

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);

        var routing = result.Value;

        Assert.InRange(routing.Team.Confidence, 0.0, 1.0);
        Assert.True(Enum.IsDefined(routing.Team.Value));
        Assert.Equal(3, routing.Team.Probabilities.Count);
        Assert.InRange(routing.Team.Probabilities[routing.Team.Value], 0.0, 1.0);
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task BadKey_IsUnauthorized()
    {
        using var client = new JevClient(new JevClientOptions { ApiKey = "invalid-key-for-live-test", MaxRetries = 0 });

        var result = await client.EvaluateAsync(Live.Request(JevProvider.TypeSafe));

        Assert.True(result.IsFailure);
        Live.LogError(_output, result.Error);
        Assert.Equal(JevErrorKind.Unauthorized, result.Error.Kind);
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task InvalidRequest_IsValidation()
    {
        using var client = new JevClient(new JevClientOptions { MaxRetries = 0 });
        var request = new SystemOneRequest
        {
            State = "Help! My payouts have been failing for 3 days and nobody answers.",
            Model = Live.Model(JevProvider.TypeSafe),
            Questions = new Dictionary<string, JevQuestion>(),
        };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsFailure);
        Live.LogError(_output, result.Error);
        Assert.Equal(JevErrorKind.Validation, result.Error.Kind);
    }
}
