using MinosRequest = Minos.DecisionRequest;
using MinosResponse = Minos.DecisionResponse;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// A Minos answer taken to upstream and back: a Minos fake, wrapped in <see cref="MinosDecisionClient"/>, wrapped in
/// <see cref="UpstreamDecisionClient"/>, returns the answers the fake gave, as docs/extensions-ai-decisions/mapping.md
/// promises for the values with no upstream slot, which travel under the <c>minos.</c> keys.
/// </summary>
/// <remarks>
/// Each answer carries values the assumptions would not give back: a confidence that is not the chosen probability, a
/// Choice whose chosen option is not the most probable, and a probability source other than <c>Model</c>. The Score
/// distributions are rounded to two places, as a Minos provider reports them, and their expected levels are off the
/// rounded distribution by up to the 0.005 the declared <see cref="Microsoft.Extensions.AI.DecisionPrecision.TwoDecimalPlaces"/>
/// allows, such as 2.97 for 0, 0, 0.02, 0.98, so the upstream answer constructors accept them and nothing is repaired.
/// </remarks>
public sealed class RoundTripTests
{
    private static readonly QuestionDefinition Urgent = QuestionDefinition.Noul("is_urgent", "Is it urgent?");
    private static readonly QuestionDefinition Spam = QuestionDefinition.Noul("is_spam", "Is it spam?", "Spam", "Not spam");
    private static readonly QuestionDefinition Team = QuestionDefinition.Choice(
        "team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", null), new OptionDefinition("sales", "Deals"));
    private static readonly QuestionDefinition Product = QuestionDefinition.Choice(
        "product", "Which product?", new OptionDefinition("pro-plan", "Pro"), new OptionDefinition("team-plan", "Team"));
    private static readonly QuestionDefinition Severity = QuestionDefinition.Score("severity", "How severe?", "None", "Low", "High", "Critical");
    private static readonly QuestionDefinition Effort = QuestionDefinition.Score("effort", "How much effort?", "Minutes", "Days");

    public static TheoryData<string> Sets => ["noul", "choice", "score", "mixed"];

    [Theory]
    [MemberData(nameof(Sets))]
    public async Task MinosToUpstreamToMinos_ReturnsTheSameAnswers(string set)
    {
        var (definition, answers) = Set(set);
        var minos = new ScriptedMinosClient("fake-minos", "model-rt")
        {
            Respond = request => new MinosResponse(
                request.Definition,
                answers,
                "model-rt",
                new DecisionUsage { InputTokens = 12, OutputTokens = 7, Cost = 0.0031 })
            {
                Id = "resp-1",
                UpstreamProvider = "provider-a",
                IsCached = true,
            },
        };
        using var client = minos.AsUpstreamDecisionClient().AsMinosDecisionClient();

        var result = await client.EvaluateAsync(new MinosRequest(definition, "ticket text") { Model = "model-rt" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var response = result.Value;
        Assert.Same(definition, response.Definition);
        AnswerAssert.Same(new MinosResponse(definition, answers).Answers, response.Answers);
        Assert.Equal("model-rt", response.Model);
        Assert.Equal("resp-1", response.Id);
        Assert.Equal(new DecisionUsage { InputTokens = 12, OutputTokens = 7, Cost = 0.0031 }, response.Usage);
        Assert.Equal("provider-a", response.UpstreamProvider);
        Assert.True(response.IsCached);

        // The fake was asked the same questions, once.
        Assert.Equal([.. definition.Questions.Select(q => q.Key)], Assert.Single(minos.Calls));
    }

    [Fact]
    public async Task ADistributionOffByMoreThanTheRounding_IsNotRepaired()
    {
        var definition = new QuestionSetDefinition(Effort);
        var minos = new ScriptedMinosClient("fake-minos")
        {
            Respond = request => new MinosResponse(request.Definition, [QuestionAnswer.Score(1, 0.9, 0.9, [0.1, 0.8])]),
        };
        using var client = minos.AsUpstreamDecisionClient().AsMinosDecisionClient();

        var result = await client.EvaluateAsync(new MinosRequest(definition, "ticket text"));

        // 0.1 and 0.8 sum to 0.9, outside two places of rounding: upstream refuses it, and it comes back as InvalidResponse.
        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
    }

    private static (QuestionSetDefinition Definition, QuestionAnswer[] Answers) Set(string set) => set switch
    {
        "noul" => (
            new QuestionSetDefinition(Urgent, Spam),
            [QuestionAnswer.Noul(0.83, ProbabilitySource.Logprobs), QuestionAnswer.Noul(0.07)]),
        "choice" => (
            new QuestionSetDefinition(Team, Product),
            [
                QuestionAnswer.Choice(1, 0.42, [0.5, 0.3, 0.2], ProbabilitySource.Verbalized),
                QuestionAnswer.Choice(0, 0.67, [0.67, 0.33]),
            ]),
        "score" => (
            new QuestionSetDefinition(Severity, Effort),
            [
                QuestionAnswer.Score(3, 2.97, 0.91, [0, 0, 0.02, 0.98], ProbabilitySource.Logprobs),
                QuestionAnswer.Score(0, 0.33, 0.6, [0.67, 0.33]),
            ]),
        _ => (
            new QuestionSetDefinition(Severity, Urgent, Team),
            [
                QuestionAnswer.Score(2, 1.9, 0.55, [0.05, 0.15, 0.65, 0.15], ProbabilitySource.Verbalized),
                QuestionAnswer.Noul(0.5),
                QuestionAnswer.Choice(2, 0.34, [0.33, 0.33, 0.34], ProbabilitySource.Logprobs),
            ]),
    };
}
