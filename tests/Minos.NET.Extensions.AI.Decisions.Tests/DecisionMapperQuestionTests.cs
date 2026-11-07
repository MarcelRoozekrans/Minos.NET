using System.Text.Json;
using Microsoft.Extensions.AI;
using MinosRequest = Minos.DecisionRequest;
using UpstreamRequest = Microsoft.Extensions.AI.DecisionRequest;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// The question, state and request conversion of <see cref="DecisionMapper"/>, both ways, with a case for each row the
/// mapping page marks as a loss and each refusal it lists before sending.
/// </summary>
public sealed class DecisionMapperQuestionTests
{
    private const string Provider = "Upstream";

    // Question text and state that must never reach an error message.
    private const string Secret = "secret-7f3a";

    private static readonly DecisionContent JsonInstructions = DecisionContent.FromUtf8Json("""{"ask":"is it spam"}"""u8);

    // ---- Minos to upstream ----

    [Fact]
    public void ANoulAChoiceAndAScore_ConvertToUpstreamQuestions_InOrder()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Noul("urgent", "Is it urgent?", "Today", "Can wait"),
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", "Bugs")),
            QuestionDefinition.Score("priority", "How soon?", "Later", "This week", "Today"));

        var questions = Success(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.Equal(3, questions.Count);
        var noul = Assert.IsType<BinaryDecisionQuestion>(questions[0]);
        Assert.Equal(("urgent", DecisionKind.Binary, "Is it urgent?", "Today", "Can wait"), (noul.Id, noul.Kind, noul.Instructions, noul.TrueDescription, noul.FalseDescription));

        var choice = Assert.IsType<ChoiceDecisionQuestion>(questions[1]);
        Assert.Equal(("team", DecisionKind.Choice, "Which team?"), (choice.Id, choice.Kind, choice.Instructions));
        Assert.Equal([("billing", "Payments"), ("tech", "Bugs")], choice.Candidates.Select(c => (c.Id, c.Description)));

        var score = Assert.IsType<ScoreDecisionQuestion>(questions[2]);
        Assert.Equal(("priority", DecisionKind.Score, "How soon?"), (score.Id, score.Kind, score.Instructions));
        Assert.Equal([("0", "Later"), ("1", "This week"), ("2", "Today")], score.Levels.Select(l => (l.Id, l.Description)));
    }

    [Fact]
    public void ANoulAChoiceAndAScore_ComeBackTheSame_AfterARoundTrip()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Noul("urgent", "Is it urgent?", "Today", "Can wait"),
            QuestionDefinition.Noul("plain", "Is it plain?"),
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", "Bugs")),
            QuestionDefinition.Score("priority", "How soon?", "Later", "This week", "Today"));

        var back = DecisionMapper.ToMinosDefinition(Success(DecisionMapper.ToUpstreamQuestions(definition, Provider)));

        AssertSameQuestions(definition, back);
    }

    [Fact]
    public void AnOptionWithoutACriterion_IsSentWithItsKeyAsTheDescription()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", null), new OptionDefinition("tech", "Bugs")));

        var choice = Assert.IsType<ChoiceDecisionQuestion>(Success(DecisionMapper.ToUpstreamQuestions(definition, Provider))[0]);

        Assert.Equal([("billing", "billing"), ("tech", "Bugs")], choice.Candidates.Select(c => (c.Id, c.Description)));

        // The loss: the option comes back described by its key, not undescribed.
        var back = DecisionMapper.ToMinosDefinition([choice]).Questions[0].Options[0];
        Assert.Equal("billing", back.Criterion?.Description);
    }

    [Fact]
    public void JsonInstructions_AreSentAsTheirJsonText_AndComeBackAsText()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Noul("spam", JsonInstructions),
            QuestionDefinition.Choice("team", JsonInstructions, new OptionDefinition("a", "A")),
            QuestionDefinition.Score("level", JsonInstructions, "Low", "High"));

        var questions = Success(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.All(questions, question => Assert.Equal("""{"ask":"is it spam"}""", question.Instructions));

        // The loss: the instructions come back as text, so the provider no longer knows they were JSON.
        var back = DecisionMapper.ToMinosDefinition(questions);
        Assert.All(back.Questions, question =>
        {
            Assert.True(question.Instructions.TryGetString(out var text));
            Assert.Equal("""{"ask":"is it spam"}""", text);
        });
    }

    [Fact]
    public void JsonNoulDescriptions_AreSentAsTheirJsonText()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul(
            "spam", "Is it spam?", DecisionContent.FromUtf8Json("""{"yes":1}"""u8), DecisionContent.FromUtf8Json("[0]"u8)));

        var noul = Assert.IsType<BinaryDecisionQuestion>(Success(DecisionMapper.ToUpstreamQuestions(definition, Provider))[0]);

        Assert.Equal(("""{"yes":1}""", "[0]"), (noul.TrueDescription, noul.FalseDescription));
    }

    [Fact]
    public void JsonCriteria_AreSentAsTheirJsonText_ForAnOptionAndALevel()
    {
        var criterion = Criterion.Json(DecisionContent.FromUtf8Json("""{"means":"money"}"""u8));
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", criterion)),
            QuestionDefinition.Score("level", "How bad?", criterion, "High"));

        var questions = Success(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.Equal("""{"means":"money"}""", Assert.IsType<ChoiceDecisionQuestion>(questions[0]).Candidates[0].Description);
        Assert.Equal("""{"means":"money"}""", Assert.IsType<ScoreDecisionQuestion>(questions[1]).Levels[0].Description);
    }

    [Fact]
    public void ANoulWithBlankInstructions_AndBothDescriptions_IsSent()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("spam", " ", "Spam", "Not spam"));

        var noul = Assert.IsType<BinaryDecisionQuestion>(Success(DecisionMapper.ToUpstreamQuestions(definition, Provider))[0]);

        Assert.Equal((" ", "Spam", "Not spam"), (noul.Instructions, noul.TrueDescription, noul.FalseDescription));
    }

    [Fact]
    public void BlankChoiceAndScoreInstructions_AreSent_AsUpstreamAcceptsThem()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice("team", "", new OptionDefinition("a", "A")),
            QuestionDefinition.Score("level", " ", "Low", "High"));

        var questions = Success(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.Equal(["", " "], questions.Select(q => q.Instructions));
    }

    // ---- The refusals before sending ----

    [Fact]
    public void AnEmptyQuestionSet_IsAValidationError_WithNoFailures()
    {
        var result = DecisionMapper.ToUpstreamQuestions(new QuestionSetDefinition(), Provider);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        Assert.Equal("The request has no questions.", result.Error.Message);
        Assert.Empty(result.Error.Failures);
    }

    [Fact]
    public void AWhiteSpaceQuestionId_IsUnsupported_WithOneFailure()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul(" \t", "Is it spam?"));

        var failure = SingleFailure(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.Equal(("MIN207", " \t"), (failure.Rule, failure.QuestionKey));
    }

    [Fact]
    public void AWhiteSpaceOptionId_IsUnsupported_WithOneFailure()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition(" ", "Blank"), new OptionDefinition("tech", "Bugs")));

        var failure = SingleFailure(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.Equal(("MIN207", "team"), (failure.Rule, failure.QuestionKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankNoulDescription_IsUnsupported(string blank)
    {
        var whenTrue = new QuestionSetDefinition(QuestionDefinition.Noul("spam", "Is it spam?", whenTrue: blank));
        var whenFalse = new QuestionSetDefinition(QuestionDefinition.Noul("spam", "Is it spam?", whenFalse: blank));

        Assert.Equal(("MIN208", "spam"), Pair(SingleFailure(DecisionMapper.ToUpstreamQuestions(whenTrue, Provider))));
        Assert.Equal(("MIN208", "spam"), Pair(SingleFailure(DecisionMapper.ToUpstreamQuestions(whenFalse, Provider))));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    public void ABlankTextCriterion_IsUnsupported_ForAnOptionAndALevel(string blank)
    {
        var choice = new QuestionSetDefinition(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("a", blank)));
        var score = new QuestionSetDefinition(QuestionDefinition.Score("level", "How bad?", blank, "High"));

        Assert.Equal(("MIN208", "team"), Pair(SingleFailure(DecisionMapper.ToUpstreamQuestions(choice, Provider))));
        Assert.Equal(("MIN208", "level"), Pair(SingleFailure(DecisionMapper.ToUpstreamQuestions(score, Provider))));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Spam", null)]
    [InlineData(null, "Not spam")]
    public void ANoulWithBlankInstructions_AndNoDescriptionPair_IsUnsupported(string? whenTrue, string? whenFalse)
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul(
            "spam", " ", whenTrue is null ? (DecisionContent?)null : whenTrue, whenFalse is null ? (DecisionContent?)null : whenFalse));

        var failure = SingleFailure(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.Equal(("MIN209", "spam"), Pair(failure));
    }

    [Fact]
    public void ANoulWithBlankInstructions_AndABlankDescription_FailsBothRules()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("spam", "", "Spam", " "));

        var failures = Failures(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        Assert.Equal([("MIN208", "spam"), ("MIN209", "spam")], failures.Select(Pair));
    }

    [Fact]
    public void ANoulWithJsonInstructions_AndNoDescriptions_IsSent()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("spam", JsonInstructions));

        Assert.True(DecisionMapper.ToUpstreamQuestions(definition, Provider).IsSuccess);
    }

    [Fact]
    public void AOneLevelScore_IsUnsupported()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Score("level", "How bad?", "Only"));

        Assert.Equal(("MIN210", "level"), Pair(SingleFailure(DecisionMapper.ToUpstreamQuestions(definition, Provider))));
    }

    [Fact]
    public void ExamplesOrNotForOnACriterion_IsUnsupported_ForAnOptionAndALevel()
    {
        var examples = Criterion.Text("Payments").WithExamples("A double charge");
        var notFor = Criterion.Text("Payments").WithNotFor("Pricing");
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", examples), new OptionDefinition("sales", notFor)),
            QuestionDefinition.Score("level", "How bad?", "Low", notFor));

        var failures = Failures(DecisionMapper.ToUpstreamQuestions(definition, Provider));

        // One failure per question and rule, as OpenAI's MIN205 is.
        Assert.Equal([("MIN205", "team"), ("MIN205", "level")], failures.Select(Pair));
    }

    [Fact]
    public void Images_AreUnsupported_WithASetLevelFailure()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("spam", "Is it spam?"));
        var request = new MinosRequest(definition, "state") { Images = [DecisionImage.FromBytes(new byte[] { 1, 2, 3 }, "image/png")] };

        var result = DecisionMapper.ToUpstream(request, Provider);

        Assert.True(result.IsFailure);
        var failure = Assert.Single(result.Error.Failures);
        Assert.Equal(("MIN206", null), (failure.Rule, failure.QuestionKey));
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
    }

    [Fact]
    public void EveryProblem_IsOneFailure_ImagesFirst_ThenInQuestionOrder_AndTheMessageIsTheFirsts()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Score(" ", Secret, Secret.Trim() + " ", "x"),
            QuestionDefinition.Noul("spam", " "),
            QuestionDefinition.Choice("team", Secret, new OptionDefinition(" ", Criterion.Text(" ").WithExamples(Secret))),
            QuestionDefinition.Score("level", Secret, Criterion.Text(Secret)));
        var request = new MinosRequest(definition, Secret) { Images = [DecisionImage.FromBytes(new byte[] { 1 }, "image/png")] };

        var result = DecisionMapper.ToUpstream(request, Provider);

        Assert.True(result.IsFailure);
        Assert.Equal(
            [("MIN206", null), ("MIN207", " "), ("MIN209", "spam"), ("MIN205", "team"), ("MIN207", "team"), ("MIN208", "team"), ("MIN210", "level")],
            result.Error.Failures.Select(Pair));
        Assert.Equal(result.Error.Failures[0].Message, result.Error.Message);
        Assert.All(result.Error.Failures, failure =>
        {
            Assert.StartsWith(Provider + " ", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, failure.Message, StringComparison.Ordinal);
        });
    }

    // ---- The request: state, model, retry attempt ----

    [Fact]
    public void TextState_IsSentAsAJsonString_AndTheModelAsTheOptionsModelId()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("spam", "Is it spam?"));
        var request = new MinosRequest(definition, "Buy now \"cheap\"") { Model = "m-1", RetryAttempt = 2 };

        var call = Success(DecisionMapper.ToUpstream(request, Provider));

        Assert.Equal(JsonValueKind.String, call.Request.State.ValueKind);
        Assert.Equal("Buy now \"cheap\"", call.Request.State.GetString());
        Assert.Equal("m-1", call.Options.ModelId);
        Assert.Null(call.Options.AdditionalProperties);
        Assert.Equal("spam", Assert.Single(call.Request.Questions).Id);
    }

    [Theory]
    [InlineData("""{"subject":"hi","n":[1,2]}""")]
    [InlineData("""[1,"two"]""")]
    public void JsonState_IsSentAsItIs(string json)
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("spam", "Is it spam?"));
        var request = new MinosRequest(definition, DecisionContent.FromUtf8Json(System.Text.Encoding.UTF8.GetBytes(json)));

        var call = Success(DecisionMapper.ToUpstream(request, Provider));

        using var expected = JsonDocument.Parse(json);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, call.Request.State));
        Assert.Null(call.Options.ModelId);
    }

    [Fact]
    public void TextStateWithALoneSurrogate_IsSentWithTheReplacementCharacter()
    {
        // Built here, not in theory data: xUnit turns a lone surrogate in theory data into U+FFFD before the test runs.
        var text = "a" + '\uD800' + "b";

        var state = DecisionMapper.ToUpstreamState(text);

        Assert.Equal("a\uFFFDb", state.GetString());
    }

    [Fact]
    public void TheUnsupportedErrorTable_OnTheProvidersPage_HasTheMappersMessages()
    {
        var rows = File.ReadLines(Path.Combine(RepositoryRoot(), "docs", "providers.md"))
            .Where(line => line.StartsWith("| `MIN2", StringComparison.Ordinal))
            .Select(line => line.Split(" | "))
            .Where(cells => cells.Length == 4)
            .ToDictionary(cells => cells[0].Trim('|', ' ', '`'), cells => cells[3].TrimEnd('|', ' ').Trim('`'));
        QuestionSetDefinition[] sets =
        [
            new(QuestionDefinition.Noul(" ", "Is it spam?")),
            new(QuestionDefinition.Noul("spam", "Is it spam?", whenTrue: " ")),
            new(QuestionDefinition.Noul("spam", " ")),
            new(QuestionDefinition.Score("level", "How bad?", "Only")),
        ];

        foreach (var set in sets)
        {
            var failure = SingleFailure(DecisionMapper.ToUpstreamQuestions(set, Provider));
            Assert.Equal(rows[failure.Rule], failure.Message);
        }

        var detail = SingleFailure(DecisionMapper.ToUpstreamQuestions(
            new QuestionSetDefinition(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("a", Criterion.Text("A").WithNotFor("B")))),
            Provider));
        Assert.Equal(rows["MIN205"].Replace("OpenAI", Provider, StringComparison.Ordinal), detail.Message);

        // MIN201's wording is the native clients' own; the adapter's copy must not drift from it.
        var kind = SingleFailure(DecisionMapper.ToUpstream(
            new MinosRequest(new QuestionSetDefinition(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("a", "A"), new OptionDefinition("b", "B"))), "state"),
            Provider,
            QuestionKinds.Noul | QuestionKinds.Score));
        Assert.Equal(("MIN201", "team"), Pair(kind));
        Assert.Equal(rows["MIN201"].Replace("Custom", Provider, StringComparison.Ordinal), kind.Message);
    }

    // ---- Upstream to Minos ----

    [Fact]
    public void UpstreamQuestions_ConvertToAMinosDefinition_InOrder()
    {
        DecisionQuestion[] questions =
        [
            new BinaryDecisionQuestion("urgent", "Is it urgent?", "Today", "Can wait"),
            new BinaryDecisionQuestion("plain", "Is it plain?"),
            new ChoiceDecisionQuestion("team", "Which team?", [new DecisionCandidate("billing", "Payments"), new DecisionCandidate("tech", "Bugs")]),
            new ScoreDecisionQuestion("priority", "How soon?", [new DecisionScoreLevel("low", "Later"), new DecisionScoreLevel("high", "Today")]),
        ];

        var definition = DecisionMapper.ToMinosDefinition(questions);

        var expected = new QuestionSetDefinition(
            QuestionDefinition.Noul("urgent", "Is it urgent?", "Today", "Can wait"),
            QuestionDefinition.Noul("plain", "Is it plain?"),
            QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", "Bugs")),
            QuestionDefinition.Score("priority", "How soon?", "Later", "Today"));
        AssertSameQuestions(expected, definition);
    }

    [Fact]
    public void UpstreamScoreLevelIds_AreNotInTheMinosDefinition_TheLevelsKeepTheirOrder()
    {
        var score = new ScoreDecisionQuestion("priority", "How soon?", [new DecisionScoreLevel("low", "Later"), new DecisionScoreLevel("high", "Today")]);

        var question = DecisionMapper.ToMinosDefinition([score]).Questions[0];

        // The loss: Minos keys levels by position. The upstream ids stay on the upstream question, where the answer
        // conversion reads them back.
        Assert.Equal([("0", "Later"), ("1", "Today")], question.Options.Select(o => (o.Key, o.Criterion?.Description)));
    }

    [Fact]
    public void ABinaryQuestionWithoutInstructions_BecomesANoulWithEmptyTextInstructions()
    {
        var binary = new BinaryDecisionQuestion("spam", null, "Spam", "Not spam");

        var noul = DecisionMapper.ToMinosDefinition([binary]).Questions[0];

        Assert.True(noul.Instructions.TryGetString(out var text));
        Assert.Equal(string.Empty, text);
        Assert.Equal(("Spam", "Not spam"), (noul.WhenTrue?.ToString(), noul.WhenFalse?.ToString()));
    }

    [Fact]
    public void UpstreamDescriptions_BecomeTextCriteria_EvenWhenTheyLookLikeJson()
    {
        var choice = new ChoiceDecisionQuestion("team", """{"ask":1}""", [new DecisionCandidate("a", """{"means":"money"}""")]);

        var question = DecisionMapper.ToMinosDefinition([choice]).Questions[0];

        Assert.True(question.Instructions.TryGetString(out _));
        Assert.Equal("""{"means":"money"}""", question.Options[0].Criterion?.Description);
        Assert.Null(question.Options[0].Criterion?.JsonContent);
    }

    [Fact]
    public void AnUpstreamRequest_BecomesAMinosRequest_WithTheModelAndNoRetryAttempt()
    {
        using var state = JsonDocument.Parse("""{"subject":"hi"}""");
        var upstream = new UpstreamRequest(state.RootElement, [new BinaryDecisionQuestion("spam", "Is it spam?")]);

        var request = DecisionMapper.ToMinos(upstream, new DecisionOptions { ModelId = "m-1" });

        Assert.Equal("spam", Assert.Single(request.Definition.Questions).Key);
        Assert.True(request.State.TryGetJson(out var json));
        Assert.True(JsonElement.DeepEquals(state.RootElement, json));
        Assert.Equal(("m-1", 0), (request.Model, request.RetryAttempt));
        Assert.Empty(request.Images);
        Assert.Null(DecisionMapper.ToMinos(upstream, options: null).Model);
    }

    [Fact]
    public void AJsonStringState_BecomesText()
    {
        using var state = JsonDocument.Parse("\"Buy now\"");

        var content = DecisionMapper.ToMinosState(state.RootElement);

        Assert.True(content.TryGetString(out var text));
        Assert.Equal("Buy now", text);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public void AStateMinosCannotHold_ThrowsANonTransientClientException_WithoutTheState(string json)
    {
        using var state = JsonDocument.Parse(json);

        var exception = Assert.Throws<DecisionClientException>(() => DecisionMapper.ToMinosState(state.RootElement));

        Assert.False(exception.IsTransient);
        Assert.DoesNotContain(json, exception.Message, StringComparison.Ordinal);
    }

    // ---- Helpers ----

    private static T Success<T>(ZeroAlloc.Results.Result<T, DecisionError> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static IReadOnlyList<QuestionFailure> Failures<T>(ZeroAlloc.Results.Result<T, DecisionError> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(result.Error.Failures[0].Message, result.Error.Message);
        return result.Error.Failures;
    }

    private static QuestionFailure SingleFailure<T>(ZeroAlloc.Results.Result<T, DecisionError> result)
        => Assert.Single(Failures(result));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Minos.NET.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static (string Rule, string? QuestionKey) Pair(QuestionFailure failure) => (failure.Rule, failure.QuestionKey);

    private static void AssertSameQuestions(QuestionSetDefinition expected, QuestionSetDefinition actual)
    {
        Assert.Equal(expected.Questions.Count, actual.Questions.Count);
        for (var i = 0; i < expected.Questions.Count; i++)
        {
            var e = expected.Questions[i];
            var a = actual.Questions[i];
            Assert.Equal((e.Key, e.Kind), (a.Key, a.Kind));
            Assert.Equal(e.Instructions, a.Instructions);
            Assert.Equal(e.WhenTrue, a.WhenTrue);
            Assert.Equal(e.WhenFalse, a.WhenFalse);
            Assert.Equal(e.Options.Count, a.Options.Count);
            for (var j = 0; j < e.Options.Count; j++)
            {
                Assert.Equal(
                    (e.Options[j].Key, e.Options[j].Criterion?.Description),
                    (a.Options[j].Key, a.Options[j].Criterion?.Description));
            }
        }
    }
}
