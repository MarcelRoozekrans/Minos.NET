using System.Text.Json;
using Microsoft.Extensions.AI;
using MinosResponse = Minos.DecisionResponse;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// The answer conversion of <see cref="DecisionMapper"/>, both ways, with a case for each row of the mapping page's
/// answers table that is a loss or has no counterpart.
/// </summary>
public sealed class DecisionMapperAnswerTests
{
    private const string Provider = "Upstream";

    private static readonly QuestionSetDefinition Definition = new(
        QuestionDefinition.Noul("urgent", "Is it urgent?"),
        QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", "Bugs"), new OptionDefinition("sales", "Deals")),
        QuestionDefinition.Score("priority", "How soon?", "Never", "Later", "This week", "Today"));

    // The upstream questions an upstream caller sends Over Minos: level ids of its own, not Minos's position keys.
    private static readonly DecisionQuestion[] UpstreamQuestions =
    [
        new BinaryDecisionQuestion("urgent", "Is it urgent?"),
        new ChoiceDecisionQuestion("team", "Which team?", [new("billing", "Payments"), new("tech", "Bugs"), new("sales", "Deals")]),
        new ScoreDecisionQuestion("priority", "How soon?", [new("never", "Never"), new("later", "Later"), new("week", "This week"), new("today", "Today")]),
    ];

    // ---- Minos to upstream: Over Minos ----

    [Fact]
    public void AnAnswerForAnotherQuestion_ThrowsAProtocolException_ThatNamesNoKey()
    {
        // The right kind and option count, but the key of another question: Over Minos pairs by position, so the key is checked.
        var error = Assert.Throws<DecisionProtocolException>(
            () => DecisionMapper.ToUpstreamAnswer(Keyed(QuestionAnswer.Noul(0.9), "other"), UpstreamQuestions[0]));

        Assert.Equal("The answers do not match the questions they belong to.", error.Message);
        Assert.Throws<DecisionProtocolException>(() => DecisionMapper.ToUpstreamAnswer(QuestionAnswer.Noul(0.9), UpstreamQuestions[0]));
    }

    [Fact]
    public void ANoulAnswer_BecomesABinaryAnswer_WithItsProbabilityAndSource()
    {
        var answer = Assert.IsType<BinaryDecisionAnswer>(
            DecisionMapper.ToUpstreamAnswer(Keyed(QuestionAnswer.Noul(0.83, ProbabilitySource.Logprobs), UpstreamQuestions[0].Id), UpstreamQuestions[0]));

        Assert.Equal(("urgent", DecisionKind.Binary, 0.83), (answer.QuestionId, answer.Kind, answer.TrueProbability));
        Assert.Equal(DecisionPrecision.TwoDecimalPlaces, answer.Precision);
        Assert.Null(answer.RawRepresentation);

        // A Noul has no confidence and no chosen level, so only the source is written.
        var properties = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(answer.AdditionalProperties);
        Assert.Equal(["minos.probabilitySource"], properties.Keys);
        Assert.Equal("logprobs", properties["minos.probabilitySource"]);
    }

    [Fact]
    public void AChoiceAnswer_BecomesAChoiceAnswer_WithTheSelectedIdProbabilitiesConfidenceAndSource()
    {
        // The chosen option is not the most probable one; the adapter keeps it as it is.
        var minos = QuestionAnswer.Choice(1, 0.4, [0.5, 0.3, 0.2], ProbabilitySource.Verbalized);

        var answer = Assert.IsType<ChoiceDecisionAnswer>(DecisionMapper.ToUpstreamAnswer(Keyed(minos, UpstreamQuestions[1].Id), UpstreamQuestions[1]));

        Assert.Equal(("team", DecisionKind.Choice, "tech"), (answer.QuestionId, answer.Kind, answer.SelectedCandidateId));
        Assert.Equal([("billing", 0.5), ("tech", 0.3), ("sales", 0.2)], answer.Probabilities.Select(p => (p.Id, p.Value)));
        Assert.Equal(DecisionPrecision.TwoDecimalPlaces, answer.Precision);

        var properties = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(answer.AdditionalProperties);
        Assert.Equal(["minos.confidence", "minos.probabilitySource"], properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0.4, properties["minos.confidence"]);
        Assert.Equal("verbalized", properties["minos.probabilitySource"]);
    }

    [Fact]
    public void AScoreAnswer_BecomesAScoreAnswer_WithTheExpectedLevelAsScore_AndTheLevelIdsOfTheUpstreamQuestion()
    {
        var minos = QuestionAnswer.Score(1, 1.4, 0.55, [0.1, 0.55, 0.2, 0.15]);

        var answer = Assert.IsType<ScoreDecisionAnswer>(DecisionMapper.ToUpstreamAnswer(Keyed(minos, UpstreamQuestions[2].Id), UpstreamQuestions[2]));

        Assert.Equal(("priority", DecisionKind.Score, 1.4), (answer.QuestionId, answer.Kind, answer.Score));
        Assert.Equal([("never", 0.1), ("later", 0.55), ("week", 0.2), ("today", 0.15)], answer.Probabilities.Select(p => (p.Id, p.Value)));
        Assert.Equal(1.4, answer.ExpectedScore, 9);
        Assert.Equal(DecisionPrecision.TwoDecimalPlaces, answer.Precision);

        var properties = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(answer.AdditionalProperties);
        Assert.Equal(["minos.chosenLevel", "minos.confidence", "minos.probabilitySource"], properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1, properties["minos.chosenLevel"]);
        Assert.Equal(0.55, properties["minos.confidence"]);
        Assert.Equal("model", properties["minos.probabilitySource"]);
    }

    [Fact]
    public void AScoreFromTheGuardrailsRecordings_PassesUnderTwoDecimalPlaces()
    {
        // A recorded answer: the provider's expected level is 2.97, the rounded distribution's is 2.98.
        double[] probabilities = [0, 0, 0.02, 0.98];
        var question = new ScoreDecisionQuestion("severity", null, [new("0", "None"), new("1", "Low"), new("2", "High"), new("3", "Severe")]);

        var answer = Assert.IsType<ScoreDecisionAnswer>(
            DecisionMapper.ToUpstreamAnswer(Keyed(QuestionAnswer.Score(3, 2.97, 0.98, probabilities), question.Id), question));

        Assert.Equal(2.97, answer.Score);
        Assert.Equal(DecisionPrecision.TwoDecimalPlaces, answer.Precision);

        // The same answer declared HighPrecision breaks upstream's agreement check, which is why the precision is declared.
        var exact = probabilities.Select((p, i) => new DecisionProbability(i.ToString(System.Globalization.CultureInfo.InvariantCulture), p)).ToArray();
        Assert.Throws<DecisionProtocolException>(() => new ScoreDecisionAnswer("severity", 2.97, exact, DecisionPrecision.HighPrecision));
    }

    [Fact]
    public void ARoundedDistribution_WithinTheTolerance_IsKeptAsReported()
    {
        // Sums to 0.99: within 3 x 0.005 + 0.000001 of 1, and not repaired.
        var answer = Assert.IsType<ChoiceDecisionAnswer>(
            DecisionMapper.ToUpstreamAnswer(Keyed(QuestionAnswer.Choice(0, 0.33, [0.33, 0.33, 0.33]), UpstreamQuestions[1].Id), UpstreamQuestions[1]));

        Assert.Equal([0.33, 0.33, 0.33], answer.Probabilities.Select(p => p.Value));
    }

    [Fact]
    public void ADistributionOutsideTheTolerance_ThrowsUpstreamsProtocolException_NotASilentFix()
    {
        // Sums to 0.9, further from 1 than 3 x 0.005 + 0.000001.
        var choice = QuestionAnswer.Choice(0, 0.5, [0.5, 0.3, 0.1]);
        var choiceError = Assert.Throws<DecisionProtocolException>(() => DecisionMapper.ToUpstreamAnswer(Keyed(choice, UpstreamQuestions[1].Id), UpstreamQuestions[1]));
        Assert.Equal("A probability distribution must sum to 1 within the bound allowed by its declared rounding.", choiceError.Message);

        var score = QuestionAnswer.Score(0, 0.5, 0.6, [0.6, 0.6, 0, 0]);
        Assert.Throws<DecisionProtocolException>(() => DecisionMapper.ToUpstreamAnswer(Keyed(score, UpstreamQuestions[2].Id), UpstreamQuestions[2]));
    }

    [Fact]
    public void AScoreThatDisagreesWithItsDistribution_ThrowsUpstreamsProtocolException()
    {
        // The distribution puts everything on level 0, so no rounding allows an expected level of 2.
        var score = QuestionAnswer.Score(0, 2, 1, [1, 0, 0, 0]);

        var error = Assert.Throws<DecisionProtocolException>(() => DecisionMapper.ToUpstreamAnswer(Keyed(score, UpstreamQuestions[2].Id), UpstreamQuestions[2]));
        Assert.Equal("The score does not agree with its probability distribution.", error.Message);
    }

    [Theory]
    [InlineData(3.5)]
    [InlineData(-0.5)]
    public void AnExpectedLevelOutsideTheLevelRange_ThrowsUpstreamsProtocolException(double expected)
    {
        var score = QuestionAnswer.Score(0, expected, 0.25, [0.25, 0.25, 0.25, 0.25]);

        var error = Assert.Throws<DecisionProtocolException>(() => DecisionMapper.ToUpstreamAnswer(Keyed(score, UpstreamQuestions[2].Id), UpstreamQuestions[2]));
        Assert.Equal("A score must be finite and within the ordinal level range.", error.Message);
    }

    [Fact]
    public void AnAnswerOfAnotherKind_ThanItsQuestion_ThrowsAProtocolException()
    {
        var error = Assert.Throws<DecisionProtocolException>(
            () => DecisionMapper.ToUpstreamAnswer(Keyed(QuestionAnswer.Noul(0.9), UpstreamQuestions[1].Id), UpstreamQuestions[1]));
        Assert.DoesNotContain("0.9", error.Message, StringComparison.Ordinal);

        Assert.Throws<DecisionProtocolException>(
            () => DecisionMapper.ToUpstreamAnswer(Keyed(QuestionAnswer.Choice(0, 1, [1, 0]), UpstreamQuestions[1].Id), UpstreamQuestions[1]));
    }

    [Fact]
    public void AMinosResponse_ConvertsToOneUpstreamAnswerPerQuestion_InOrder()
    {
        var definition = DecisionMapper.ToMinosDefinition(UpstreamQuestions);
        var response = new MinosResponse(definition, [
            QuestionAnswer.Noul(0.2),
            QuestionAnswer.Choice(2, 0.7, [0.1, 0.2, 0.7]),
            QuestionAnswer.Score(3, 2.88, 0.9, [0, 0.02, 0.08, 0.9]),
        ]);

        var answers = DecisionMapper.ToUpstreamAnswers(response, UpstreamQuestions);

        Assert.Equal(["urgent", "team", "priority"], answers.Select(a => a.QuestionId));
        Assert.All(answers, a => Assert.Equal(DecisionPrecision.TwoDecimalPlaces, a.Precision));

        // Upstream's own response checks the answers against the request: ids, kinds and probability ids in order.
        _ = new Microsoft.Extensions.AI.DecisionResponse(
            new Microsoft.Extensions.AI.DecisionRequest(JsonSerializer.SerializeToElement("s", DecisionJsonContext.Default.String), UpstreamQuestions),
            answers);
    }

    // A factory answer has no key; a response gives it the key of its question. This builds a one-question response
    // whose question has the answer's kind and option count and the given key, so the answer carries that key.
    private static QuestionAnswer Keyed(QuestionAnswer answer, string key)
    {
        var count = answer.Probabilities.Length;
        var question = answer.Kind switch
        {
            QuestionKind.Noul => QuestionDefinition.Noul(key, "Question?"),
            QuestionKind.Choice => QuestionDefinition.Choice(
                key, "Question?", [.. Enumerable.Range(0, count).Select(i => new OptionDefinition("o" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Option"))]),
            _ => QuestionDefinition.Score(key, "Question?", [.. Enumerable.Range(0, count).Select(_ => (Criterion)"Level")]),
        };

        return new MinosResponse(new QuestionSetDefinition(question), [answer]).Answers[0];
    }

    // ---- Upstream to Minos: Over upstream ----

    [Fact]
    public void ABinaryAnswer_BecomesANoulAnswer_WithModelAsTheAssumedSource()
    {
        var answer = Success(DecisionMapper.ToMinosAnswer(new BinaryDecisionAnswer("urgent", 0.71), Definition.Questions[0]));

        Assert.Equal((QuestionKind.Noul, 0.71, -1, 0.0), (answer.Kind, answer.Probability, answer.ChosenIndex, answer.Confidence));
        Assert.Equal(ProbabilitySource.Model, answer.ProbabilitySource);
    }

    [Fact]
    public void AChoiceAnswer_BecomesAChoiceAnswer_AtTheSelectedPosition_WithItsProbabilityAsTheAssumedConfidence()
    {
        // The selected candidate is not the most probable one: it is kept, and its own probability is the confidence.
        var upstream = new ChoiceDecisionAnswer("team", "tech", [new("billing", 0.6), new("tech", 0.3), new("sales", 0.1)]);

        var answer = Success(DecisionMapper.ToMinosAnswer(upstream, Definition.Questions[1]));

        Assert.Equal((QuestionKind.Choice, 1, 0.3), (answer.Kind, answer.ChosenIndex, answer.Confidence));
        Assert.Equal([0.6, 0.3, 0.1], answer.Probabilities.ToArray());
        Assert.Equal(ProbabilitySource.Model, answer.ProbabilitySource);
    }

    [Fact]
    public void AScoreAnswer_WithoutKeys_TakesTheMostProbableLevel_TheLowerOnATie_AndItsProbabilityAsConfidence()
    {
        var upstream = new ScoreDecisionAnswer("priority", 1.65, [new("0", 0.1), new("1", 0.35), new("2", 0.35), new("3", 0.2)]);

        var answer = Success(DecisionMapper.ToMinosAnswer(upstream, Definition.Questions[2]));

        Assert.Equal((QuestionKind.Score, 1, 0.35), (answer.Kind, answer.ChosenIndex, answer.Confidence));
        Assert.Equal(1.65, answer.Expected);
        Assert.Equal([0.1, 0.35, 0.35, 0.2], answer.Probabilities.ToArray());
        Assert.Equal(ProbabilitySource.Model, answer.ProbabilitySource);
    }

    [Fact]
    public void AScoreAnswer_WithoutAScore_TakesTheExpectedScoreAsTheExpectedLevel()
    {
        var upstream = new ScoreDecisionAnswer("priority", null, [new("0", 0), new("1", 0.5), new("2", 0.25), new("3", 0.25)]);

        var answer = Success(DecisionMapper.ToMinosAnswer(upstream, Definition.Questions[2]));

        Assert.Equal(upstream.ExpectedScore, answer.Expected);
        Assert.Equal(1.75, answer.Expected, 9);
    }

    [Theory]
    [InlineData(DecisionPrecision.HighPrecision)]
    [InlineData(DecisionPrecision.FourDecimalPlaces)]
    [InlineData(DecisionPrecision.TwoDecimalPlaces)]
    public void TheProbabilitySource_IsModel_WithoutTheKey_WhateverThePrecision(DecisionPrecision precision)
    {
        DecisionAnswer[] upstream =
        [
            new BinaryDecisionAnswer("urgent", 0.5, precision),
            new ChoiceDecisionAnswer("team", "billing", [new("billing", 0.5), new("tech", 0.25), new("sales", 0.25)], precision),
            new ScoreDecisionAnswer("priority", null, [new("0", 0.25), new("1", 0.25), new("2", 0.25), new("3", 0.25)], precision),
        ];

        var answers = Success(DecisionMapper.ToMinosAnswers(upstream, Definition));

        Assert.All(answers, a => Assert.Equal(ProbabilitySource.Model, a.ProbabilitySource));
    }

    [Fact]
    public void TheMinosKeys_AreReadBack_OverTheAssumptions()
    {
        var properties = Properties(("minos.chosenLevel", 3), ("minos.confidence", 0.42), ("minos.probabilitySource", "logprobs"));
        var score = new ScoreDecisionAnswer("priority", 1.65, [new("0", 0.1), new("1", 0.35), new("2", 0.35), new("3", 0.2)], additionalProperties: properties);
        var choice = new ChoiceDecisionAnswer("team", "tech", [new("billing", 0.6), new("tech", 0.3), new("sales", 0.1)], additionalProperties: Properties(("minos.confidence", 0.9), ("minos.probabilitySource", "verbalized")));
        var noul = new BinaryDecisionAnswer("urgent", 0.5, additionalProperties: Properties(("minos.probabilitySource", "verbalized")));

        var answers = Success(DecisionMapper.ToMinosAnswers([noul, choice, score], Definition));

        Assert.Equal(ProbabilitySource.Verbalized, answers[0].ProbabilitySource);
        Assert.Equal((1, 0.9, ProbabilitySource.Verbalized), (answers[1].ChosenIndex, answers[1].Confidence, answers[1].ProbabilitySource));
        Assert.Equal((3, 0.42, ProbabilitySource.Logprobs), (answers[2].ChosenIndex, answers[2].Confidence, answers[2].ProbabilitySource));
    }

    [Fact]
    public void TheMinosKeys_AreReadBack_AsJsonValues_AfterUpstreamSerialization()
    {
        // Level 2 and confidence 0.61 differ from the assumptions, level 1 and 0.35, so only the keys can produce them.
        var minos = QuestionAnswer.Score(2, 1.65, 0.61, [0.1, 0.35, 0.35, 0.2], ProbabilitySource.Logprobs);
        var questions = Success(DecisionMapper.ToUpstreamQuestions(Definition, Provider));
        var sent = DecisionMapper.ToUpstreamAnswer(Keyed(minos, questions[2].Id), questions[2]);

        var typeInfo = AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(DecisionAnswer));
        var json = JsonSerializer.Serialize(sent, typeInfo);
        var received = Assert.IsType<ScoreDecisionAnswer>(JsonSerializer.Deserialize(json, typeInfo));

        var properties = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(received.AdditionalProperties);
        Assert.All(properties.Values, value => Assert.IsType<JsonElement>(value));

        var answer = Success(DecisionMapper.ToMinosAnswer(received, Definition.Questions[2]));

        Assert.Equal((2, 0.61, ProbabilitySource.Logprobs), (answer.ChosenIndex, answer.Confidence, answer.ProbabilitySource));
    }

    [Fact]
    public void OtherProperties_AndThePrecision_AreDropped()
    {
        var upstream = new BinaryDecisionAnswer("urgent", 0.5, DecisionPrecision.FourDecimalPlaces, rawRepresentation: new object(), Properties(("vendor.trace", "x")));

        var answer = Success(DecisionMapper.ToMinosAnswer(upstream, Definition.Questions[0]));

        Assert.Equal((0.5, ProbabilitySource.Model), (answer.Probability, answer.ProbabilitySource));
    }

    [Theory]
    [InlineData("minos.chosenLevel", "4")]
    [InlineData("minos.chosenLevel", "-1")]
    [InlineData("minos.chosenLevel", "1.5")]
    [InlineData("minos.chosenLevel", "\"two\"")]
    [InlineData("minos.confidence", "1.01")]
    [InlineData("minos.confidence", "1e400")]
    [InlineData("minos.confidence", "\"high\"")]
    [InlineData("minos.probabilitySource", "\"guessed\"")]
    [InlineData("minos.probabilitySource", "1")]
    public void AMalformedMinosKey_IsAnInvalidResponse_WithoutItsValueInTheMessage(string key, string json)
    {
        var properties = Properties((key, JsonDocument.Parse(json).RootElement));
        var score = new ScoreDecisionAnswer("priority", 1.65, [new("0", 0.1), new("1", 0.35), new("2", 0.35), new("3", 0.2)], additionalProperties: properties);

        var result = DecisionMapper.ToMinosAnswer(score, Definition.Questions[2]);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Contains(key, result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(json.Trim('"'), result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("minos.chosenLevel")]
    [InlineData("minos.confidence")]
    [InlineData("minos.probabilitySource")]
    public void APresentButNullMinosKey_IsAnInvalidResponse(string key)
    {
        var score = new ScoreDecisionAnswer("priority", 1.65, [new("0", 0.1), new("1", 0.35), new("2", 0.35), new("3", 0.2)], additionalProperties: Properties((key, null)));

        var result = DecisionMapper.ToMinosAnswer(score, Definition.Questions[2]);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Contains(key, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnswerThatDoesNotMatchItsQuestion_IsAnInvalidResponse()
    {
        // Upstream's response constructor checks these, so they reach the mapper only from a caller that skipped it.
        var wrongKind = DecisionMapper.ToMinosAnswer(new BinaryDecisionAnswer("team", 0.5), Definition.Questions[1]);
        var wrongId = DecisionMapper.ToMinosAnswer(new BinaryDecisionAnswer("other", 0.5), Definition.Questions[0]);
        var wrongOrder = DecisionMapper.ToMinosAnswer(
            new ChoiceDecisionAnswer("team", "tech", [new("tech", 0.6), new("billing", 0.3), new("sales", 0.1)]), Definition.Questions[1]);
        var wrongCount = DecisionMapper.ToMinosAnswers([new BinaryDecisionAnswer("urgent", 0.5)], Definition);

        Assert.True(wrongKind.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, wrongKind.Error.Kind);
        Assert.True(wrongId.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, wrongId.Error.Kind);
        Assert.True(wrongOrder.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, wrongOrder.Error.Kind);
        Assert.True(wrongCount.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, wrongCount.Error.Kind);
    }

    [Fact]
    public void ToMinosAnswers_BuildAMinosResponse_OnTheGivenDefinition()
    {
        DecisionAnswer[] upstream =
        [
            new BinaryDecisionAnswer("urgent", 0.8),
            new ChoiceDecisionAnswer("team", "sales", [new("billing", 0.1), new("tech", 0.1), new("sales", 0.8)]),
            new ScoreDecisionAnswer("priority", 2.5, [new("0", 0), new("1", 0.1), new("2", 0.3), new("3", 0.6)]),
        ];

        var response = new MinosResponse(Definition, Success(DecisionMapper.ToMinosAnswers(upstream, Definition)));

        Assert.Equal(3, response.Answers.Count);
        Assert.Equal(2, response.Answers[1].ChosenIndex);
        Assert.Equal(3, response.Answers[2].ChosenIndex);
    }

    // ---- Both ways ----

    [Fact]
    public void MinosAnswers_ComeBackTheSame_AfterARoundTripThroughUpstream()
    {
        var questions = Success(DecisionMapper.ToUpstreamQuestions(Definition, Provider));
        QuestionAnswer[] minos =
        [
            QuestionAnswer.Noul(0.37, ProbabilitySource.Verbalized),
            QuestionAnswer.Choice(1, 0.4, [0.5, 0.3, 0.2], ProbabilitySource.Logprobs),

            // Chosen level 2 is not the most probable, and the confidence is not its probability: both survive.
            QuestionAnswer.Score(2, 2.97, 0.8, [0, 0, 0.02, 0.98], ProbabilitySource.Model),
        ];

        var upstream = new DecisionAnswer[minos.Length];
        for (var i = 0; i < minos.Length; i++)
        {
            upstream[i] = DecisionMapper.ToUpstreamAnswer(Keyed(minos[i], questions[i].Id), questions[i]);
        }

        var back = Success(DecisionMapper.ToMinosAnswers(upstream, Definition));

        for (var i = 0; i < minos.Length; i++)
        {
            Assert.Equal(
                (minos[i].Kind, minos[i].ChosenIndex, minos[i].Probability, minos[i].Expected, minos[i].Confidence, minos[i].ProbabilitySource),
                (back[i].Kind, back[i].ChosenIndex, back[i].Probability, back[i].Expected, back[i].Confidence, back[i].ProbabilitySource));
            Assert.Equal(minos[i].Probabilities.ToArray(), back[i].Probabilities.ToArray());
        }
    }

    [Fact]
    public void TheKeys_AreTheOnesThePageDefines()
    {
        Assert.Equal(
            ("minos.chosenLevel", "minos.confidence", "minos.probabilitySource"),
            (MinosPropertyKeys.ChosenLevel, MinosPropertyKeys.Confidence, MinosPropertyKeys.ProbabilitySource));
        Assert.Equal(
            ("minos.upstreamProvider", "minos.path", "minos.cost", "minos.isCached"),
            (MinosPropertyKeys.UpstreamProvider, MinosPropertyKeys.Path, MinosPropertyKeys.Cost, MinosPropertyKeys.IsCached));
    }

    private static Dictionary<string, object?> Properties(params (string Key, object? Value)[] entries)
        => entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    private static T Success<T>(ZeroAlloc.Results.Result<T, DecisionError> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }
}
