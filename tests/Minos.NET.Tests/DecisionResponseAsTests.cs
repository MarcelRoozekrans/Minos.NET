using ZeroAlloc.Results;

namespace Minos.Tests;

public sealed class DecisionResponseAsTests
{
    // A Noul at 0.2, Team Technical at 0.7 and Mood Frustrated with an expected level of 1.2 at 0.6.
    private static DecisionResponse TriageResponse(QuestionSetDefinition definition)
        => new(
            definition,
            [
                QuestionAnswer.Noul(0.2),
                QuestionAnswer.Choice(1, 0.7, [0.1, 0.7, 0.1, 0.1]),
                QuestionAnswer.Score(1, 1.2, 0.6, [0.2, 0.4, 0.4], ProbabilitySource.Verbalized),
            ],
            model: "jev-latest");

    private sealed class FixedClient(DecisionResponse response) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Success(response));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task As_returns_the_same_typed_answers_as_EvaluateAsync_T()
    {
        using var client = new FixedClient(TriageResponse(TicketTriage.Definition));

        var typed = (await client.EvaluateAsync<TicketTriage>("s")).Value;
        var response = (await client.EvaluateAsync(new DecisionRequest(TicketTriage.Definition, "s"))).Value;
        var viaAs = response.As<TicketTriage>();

        Assert.Equal(typed.RequestsCredentials, viaAs.RequestsCredentials);
        Assert.Equal(typed.Team, viaAs.Team);
        Assert.Equal(typed.Mood, viaAs.Mood);
        Assert.Equal(Department.Technical, viaAs.Team.Value);
        Assert.Equal(Frustration.Frustrated, viaAs.Mood.Value);
        Assert.Equal(ProbabilitySource.Verbalized, viaAs.Mood.ProbabilitySource);
    }

    [Fact]
    public void As_works_on_a_response_built_with_the_public_constructor()
    {
        var answers = TriageResponse(TicketTriage.Definition).As<TicketTriage>();

        Assert.Equal(0.2, answers.RequestsCredentials.Probability);
        Assert.False(answers.RequestsCredentials.Value);
        Assert.Equal(Department.Technical, answers.Team.Value);
        Assert.Equal(0.7, answers.Team.Confidence);
        Assert.Equal(Frustration.Frustrated, answers.Mood.Value);
        Assert.Equal(1.2, answers.Mood.Expected);
        Assert.Equal(0.6, answers.Mood.Confidence);
    }

    [Fact]
    public async Task As_throws_InvalidOperationException_for_another_definition_instance()
    {
        var copy = new QuestionSetDefinition([.. TicketTriage.Definition.Questions]);
        var response = TriageResponse(copy);

        var fromAs = Assert.Throws<InvalidOperationException>(() => response.As<TicketTriage>());

        using var client = new FixedClient(response);
        var fromTyped = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.EvaluateAsync<TicketTriage>("s"));
        Assert.Equal(fromTyped.Message, fromAs.Message);
    }
}
