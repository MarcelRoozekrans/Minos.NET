using Minos.Tests.WireFixtureSets;

namespace Minos.Tests;

/// <summary>The per-answer mark of how probabilities were made, and how it travels from a response to the typed answers.</summary>
public sealed class ProbabilitySourceTests
{
    private static readonly double[] Three = [0.2, 0.5, 0.3];

    [Fact]
    public void Factories_DefaultToModel()
    {
        Assert.Equal(ProbabilitySource.Model, QuestionAnswer.Noul(0.7).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Model, QuestionAnswer.Choice(1, 0.5, Three).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Model, QuestionAnswer.Score(1, 1.1, 0.5, Three).ProbabilitySource);
    }

    [Fact]
    public void Response_CarriesEachAnswersSource()
    {
        var response = MixedResponse(ProbabilitySource.Logprobs, ProbabilitySource.Verbalized, ProbabilitySource.Model);

        Assert.Equal(ProbabilitySource.Logprobs, response.Answers[0].ProbabilitySource);
        Assert.Equal(ProbabilitySource.Verbalized, response.Answers[1].ProbabilitySource);
        Assert.Equal(ProbabilitySource.Model, response.Answers[2].ProbabilitySource);
    }

    [Fact]
    public void Response_ReportsTheWeakestSource()
    {
        Assert.Equal(ProbabilitySource.Verbalized, MixedResponse(ProbabilitySource.Logprobs, ProbabilitySource.Verbalized, ProbabilitySource.Model).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Logprobs, MixedResponse(ProbabilitySource.Logprobs, ProbabilitySource.Logprobs, ProbabilitySource.Logprobs).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Model, MixedResponse(ProbabilitySource.Model, ProbabilitySource.Model, ProbabilitySource.Model).ProbabilitySource);
    }

    [Fact]
    public void Response_WithNoAnswers_ReportsModel()
    {
        var response = new DecisionResponse(NoQuestionsSet.Definition, []);

        Assert.Equal(ProbabilitySource.Model, response.ProbabilitySource);
    }

    [Fact]
    public void TypedAnswers_CarryTheSource()
    {
        var response = MixedResponse(ProbabilitySource.Logprobs, ProbabilitySource.Verbalized, ProbabilitySource.Logprobs);

        var result = WfMixed.Create(response.ToAnswerSlots());

        Assert.Equal(ProbabilitySource.Logprobs, result.RequestsCredentials.ProbabilitySource);
        Assert.Equal(ProbabilitySource.Verbalized, result.Team.ProbabilitySource);
        Assert.Equal(ProbabilitySource.Logprobs, result.Urgency.ProbabilitySource);
    }

    [Fact]
    public void BuiltSetAnswers_CarryTheSource()
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("extra", "Anything else?", out var extra)
            .Choice<WfTeam>("team", "Which team?", out var team)
            .Score<WfUrgency>("urgency", "How urgent?", out var urgency, l => l
                .Level(WfUrgency.Low, "Low").Level(WfUrgency.Medium, "Medium").Level(WfUrgency.High, "High"))
            .Choice("product", "Which product?", out var product, o => o.Option("a", "A").Option("b", "B").Option("c", "C"))
            .Score("effort", "How much effort?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);
        var set = built.Value;
        var response = new DecisionResponse(
            set.Definition,
            [
                QuestionAnswer.Noul(0.4, ProbabilitySource.Verbalized),
                QuestionAnswer.Choice(1, 0.5, Three, ProbabilitySource.Logprobs),
                QuestionAnswer.Score(1, 1.1, 0.5, Three, ProbabilitySource.Verbalized),
                QuestionAnswer.Choice(2, 0.3, Three, ProbabilitySource.Logprobs),
                QuestionAnswer.Score(0, 0.9, 0.2, Three, ProbabilitySource.Model),
            ]);

        var answers = new QuestionSetAnswers(set, response.Probabilities, response.Slots);

        Assert.Equal(ProbabilitySource.Verbalized, answers.Get(extra).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Logprobs, answers.Get(team).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Verbalized, answers.Get(urgency).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Logprobs, answers.Get(product).ProbabilitySource);
        Assert.Equal(ProbabilitySource.Model, answers.Get(effort).ProbabilitySource);
    }

    [Fact]
    public void Equality_IncludesTheSource()
    {
        Assert.True(new Noul(0.7, ProbabilitySource.Logprobs) != new Noul(0.7, ProbabilitySource.Verbalized));
        Assert.True(new Noul(0.7, ProbabilitySource.Logprobs) == new Noul(0.7, ProbabilitySource.Logprobs));
        Assert.True(new Noul(0.7) == new Noul(0.7, ProbabilitySource.Model));

        var logprobs = MixedResponse(ProbabilitySource.Logprobs, ProbabilitySource.Logprobs, ProbabilitySource.Logprobs);
        var verbalized = MixedResponse(ProbabilitySource.Logprobs, ProbabilitySource.Verbalized, ProbabilitySource.Verbalized);
        var first = WfMixed.Create(logprobs.ToAnswerSlots());
        var second = WfMixed.Create(verbalized.ToAnswerSlots());

        Assert.True(first.Team != second.Team);
        Assert.True(first.Urgency != second.Urgency);
        Assert.NotEqual(first.Team.GetHashCode(), second.Team.GetHashCode());
    }

    [Fact]
    public void KeyedAnswers_Equality_IncludesTheSource()
    {
        var built = QuestionSet.CreateBuilder()
            .Choice("product", "Which product?", out var product, o => o.Option("a", "A").Option("b", "B").Option("c", "C"))
            .Score("effort", "How much effort?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);
        var set = built.Value;

        var logprobs = KeyedAnswers(set, ProbabilitySource.Logprobs);
        var sameLogprobs = KeyedAnswers(set, ProbabilitySource.Logprobs);
        var verbalized = KeyedAnswers(set, ProbabilitySource.Verbalized);

        Assert.Equal(logprobs.Get(product), sameLogprobs.Get(product));
        Assert.Equal(logprobs.Get(product).GetHashCode(), sameLogprobs.Get(product).GetHashCode());
        Assert.NotEqual(logprobs.Get(product), verbalized.Get(product));
        Assert.NotEqual(logprobs.Get(product).GetHashCode(), verbalized.Get(product).GetHashCode());

        Assert.Equal(logprobs.Get(effort), sameLogprobs.Get(effort));
        Assert.Equal(logprobs.Get(effort).GetHashCode(), sameLogprobs.Get(effort).GetHashCode());
        Assert.NotEqual(logprobs.Get(effort), verbalized.Get(effort));
        Assert.NotEqual(logprobs.Get(effort).GetHashCode(), verbalized.Get(effort).GetHashCode());
    }

    private static QuestionSetAnswers KeyedAnswers(QuestionSet set, ProbabilitySource source)
    {
        var response = new DecisionResponse(
            set.Definition,
            [QuestionAnswer.Choice(1, 0.5, Three, source), QuestionAnswer.Score(1, 1.1, 0.5, Three, source)]);
        return new QuestionSetAnswers(set, response.Probabilities, response.Slots);
    }

    private static DecisionResponse MixedResponse(ProbabilitySource noul, ProbabilitySource choice, ProbabilitySource score)
        => new(
            WfMixed.Definition,
            [
                QuestionAnswer.Noul(0.2, noul),
                QuestionAnswer.Choice(1, 0.7, Three, choice),
                QuestionAnswer.Score(2, 1.4, 0.5, Three, score),
            ]);
}
