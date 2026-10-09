using System.Text.Json;
using Xunit.Abstractions;

namespace ZeroAlloc.Jev.Live.Tests;

public sealed class TypeSafeLiveTests
{
    private static readonly string[] Teams = ["billing", "technical", "sales"];

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
        Assert.Contains(result.Value.Models, model => string.Equals(model.Name, Live.Preview, StringComparison.Ordinal));
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
        // TypeSafe's 422 lists each problem with where it is and what is wrong; the guide's example relies on it.
        Assert.Equal(422, result.Error.StatusCode);
        Assert.NotNull(result.Error.Detail);
        var detail = result.Error.Detail.Value;
        Assert.Equal(JsonValueKind.Object, detail.ValueKind);
        Assert.True(detail.TryGetProperty("detail", out var problems));
        Assert.Equal(JsonValueKind.Array, problems.ValueKind);
        Assert.True(problems.GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.Object, problems[0].ValueKind);
        Assert.True(problems[0].TryGetProperty("loc", out var loc));
        Assert.Equal(JsonValueKind.Array, loc.ValueKind);
        Assert.True(problems[0].TryGetProperty("msg", out var msg));
        Assert.Equal(JsonValueKind.String, msg.ValueKind);
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task BuiltQuestionSet_ParsesAKeyedChoice()
    {
        using var client = Live.Client(JevProvider.TypeSafe);
        var built = JevQuestionSet.CreateBuilder()
            .Choice("team", "Which team should handle this?", out var team, o => o
                .Option("billing", JevCriterion.Text("Payments, invoicing, refunds").WithExamples("I was charged twice"))
                .Option("technical", "Bugs, outages, integrations")
                .Option("sales", "Pricing, upgrades, new accounts"))
            .Build();
        Assert.True(built.IsSuccess);

        var result = await client.EvaluateAsync(built.Value, "Help! My payouts have been failing for 3 days and nobody answers.");

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);

        var answer = result.Value.Get(team);

        Assert.Contains(answer.Value, Teams);
        Assert.InRange(answer.Confidence, 0.0, 1.0);
        Assert.Equal(3, answer.Probabilities.Count);
        Assert.InRange(answer.Probabilities[answer.Value], 0.0, 1.0);

        var sum = 0.0;
        foreach (var (key, probability) in answer.Probabilities)
        {
            Assert.Contains(key, Teams);
            Assert.InRange(probability, 0.0, 1.0);
            sum += probability;
        }

        Assert.Equal(1.0, sum, 0.01);
    }
}
