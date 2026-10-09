using System.Globalization;
using System.Text;
using Minos.Validation;

namespace Minos.Tests;

/// <summary>Each rule of the spec's table: failures with their JEV id and question key, advice as warnings.</summary>
public sealed class QuestionValidationTests
{
    [Fact]
    public void ValidSet_HasNoFailuresOrWarnings()
    {
        var (failures, warnings) = QuestionValidation.Validate(
        [
            Noul("urgent", "Is it urgent?"),
            Choice("team", "Which team?", Option("billing", "Refunds"), Option("other", null)),
            Score("urgency", "How urgent?", Option("0", "Can wait"), Option("1", "Today")),
        ]);

        Assert.Empty(failures);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ChoiceWithoutOptions_FailsJev001() => AssertFailure("JEV001", "team", Choice("team", "Which team?"));

    [Fact]
    public void ScoreWithoutLevels_FailsJev002_WithoutJev005() => AssertFailure("JEV002", "urgency", Score("urgency", "How urgent?"));

    [Fact]
    public void EnumScoreOverAnEmptyEnum_FailsJev002() => AssertFailure("JEV002", "urgency", EnumScore("urgency", []));

    [Fact]
    public void EnumScore_GivingEveryMemberOnce_InAnyOrder_Passes() => AssertClean(EnumScore("urgency", ["Low", "Medium", "High"], 2, 0, 1));

    [Fact]
    public void EnumScoreMemberNotGivenALevel_FailsJev104()
    {
        var failure = OnlyFailure(EnumScore("urgency", ["Low", "Medium", "High"], 0, 2));

        Assert.Equal("JEV104", failure.Rule);
        Assert.Equal("urgency", failure.QuestionKey);
        Assert.Contains("'Medium'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumScoreWithNoLevelGiven_FailsJev104_PerMember_WithoutJev002()
    {
        var (failures, warnings) = QuestionValidation.Validate([EnumScore("urgency", ["Low", "High"])]);

        Assert.Empty(warnings);
        Assert.Equal(2, failures.Length);
        Assert.All(failures, failure => Assert.Equal(new JevQuestionFailure("JEV104", "urgency", failure.Message), failure));
    }

    [Fact]
    public void EnumScoreMemberGivenTwice_FailsJev106_Once()
    {
        var failure = OnlyFailure(EnumScore("urgency", ["Low", "High"], 0, 1, 0, 0));

        Assert.Equal("JEV106", failure.Rule);
        Assert.Equal("urgency", failure.QuestionKey);
        Assert.Contains("'Low'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateQuestionKey_FailsJev106_Once()
        => AssertFailure("JEV106", "q", Noul("q", "One?"), Noul("q", "Two?"), Noul("q", "Three?"));

    [Fact]
    public void DuplicateOptionKey_FailsJev106()
        => AssertFailure("JEV106", "team", Choice("team", "Which team?", Option("a", "A"), Option("a", "Also A")));

    [Fact]
    public void EmptyQuestionKey_FailsJev106() => AssertFailure("JEV106", string.Empty, Noul(string.Empty, "Anything?"));

    [Fact]
    public void EmptyOptionKey_FailsJev106() => AssertFailure("JEV106", "team", Choice("team", "Which team?", Option(string.Empty, "A")));

    [Fact]
    public void InstructionsDeeperThan60Levels_FailJev108() => AssertFailure("JEV108", "q", Noul("q", Nested(61)));

    [Fact]
    public void InstructionsAt60Levels_Pass() => AssertClean(Noul("q", Nested(60)));

    [Fact]
    public void DescriptionDeeperThan60Levels_FailsJev108()
        => AssertFailure("JEV108", "team", Choice("team", "Which team?", Option("a", JevCriterion.Json(Nested(61)))));

    [Fact]
    public void BlankInstructions_WarnJev003() => AssertWarning("JEV003", "q", Noul("q", "   "));

    [Fact]
    public void EmptyJsonInstructions_WarnJev003()
    {
        AssertWarning("JEV003", "q", Noul("q", JevContent.FromUtf8Json("{}"u8)));
        AssertWarning("JEV003", "q", Noul("q", JevContent.FromUtf8Json("[]"u8)));
    }

    [Fact]
    public void BlankDescription_WarnsJev003() => AssertWarning("JEV003", "team", Choice("team", "Which team?", Option("a", " ")));

    [Fact]
    public void EmptyJsonDescription_WarnsJev003()
        => AssertWarning("JEV003", "team", Choice("team", "Which team?", Option("a", JevCriterion.Json(JevContent.FromUtf8Json("{}"u8)))));

    [Fact]
    public void BlankExampleEntry_WarnsJev003()
        => AssertWarning("JEV003", "team", Choice("team", "Which team?", Option("a", JevCriterion.Text("A").WithExamples("  "))));

    [Fact]
    public void NullNotForEntry_WarnsJev003()
        => AssertWarning("JEV003", "team", Choice("team", "Which team?", Option("a", JevCriterion.Text("A").WithNotFor(null, "x"))));

    [Fact]
    public void BlankNoulDescription_WarnsJev003() => AssertWarning("JEV003", "q", Noul("q", "Is it?") with { WhenTrue = "" });

    [Fact]
    public void ScoreOutsideGuidance_WarnsJev005()
    {
        AssertWarning("JEV005", "s", Score("s", "How?", Levels(1)));
        AssertWarning("JEV005", "s", Score("s", "How?", Levels(11)));
    }

    [Fact]
    public void ChoiceOver255Options_WarnsJev005() => AssertWarning("JEV005", "c", Choice("c", "Which?", Levels(256)));

    [Fact]
    public void BlankNoulFalseDescription_WarnsJev003() => AssertWarning("JEV003", "q", Noul("q", "Is it?") with { WhenFalse = "" });

    [Fact]
    public void WhenTrueDeeperThan60Levels_FailsJev108() => AssertFailure("JEV108", "q", Noul("q", "Is it?") with { WhenTrue = Nested(61) });

    [Fact]
    public void WhenFalseDeeperThan60Levels_FailsJev108() => AssertFailure("JEV108", "q", Noul("q", "Is it?") with { WhenFalse = Nested(61) });

    [Fact]
    public void WhenTrueAndWhenFalseAt60Levels_Pass() => AssertClean(Noul("q", "Is it?") with { WhenTrue = Nested(60), WhenFalse = Nested(60) });

    [Fact]
    public void ScoreWith2And10Levels_IsClean()
    {
        AssertClean(Score("s", "How?", Levels(2)));
        AssertClean(Score("s", "How?", Levels(10)));
    }

    [Fact]
    public void ChoiceWith255Options_IsClean() => AssertClean(Choice("c", "Which?", Levels(255)));

    [Fact]
    public void OptionKeyUsedThreeTimes_FailsJev106_Once()
        => AssertFailure("JEV106", "team", Choice("team", "Which team?", Option("a", "A"), Option("a", "B"), Option("a", "C")));

    [Fact]
    public void EnumScoreWithOneOfThreeLevelsGiven_FailsJev104ForEachMissingMember_AndWarnsJev005()
    {
        var (failures, warnings) = QuestionValidation.Validate([EnumScore("urgency", ["Low", "Medium", "High"], 1)]);

        Assert.Equal(2, failures.Length);
        Assert.All(failures, failure => Assert.Equal("JEV104", failure.Rule));
        Assert.Contains("'Low'", failures[0].Message, StringComparison.Ordinal);
        Assert.Contains("'High'", failures[1].Message, StringComparison.Ordinal);
        Assert.All(failures, failure => Assert.Equal("urgency", failure.QuestionKey));
        var warning = Only(warnings);
        Assert.Equal("JEV005", warning.Rule);
        Assert.Equal("urgency", warning.QuestionKey);
    }

    private static void AssertFailure(string rule, string key, params QuestionSpec[] questions)
    {
        var (failures, warnings) = QuestionValidation.Validate(questions);

        Assert.Empty(warnings);
        var failure = Only(failures);
        Assert.Equal(rule, failure.Rule);
        Assert.Equal(key, failure.QuestionKey);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }

    private static void AssertWarning(string rule, string key, QuestionSpec question)
    {
        var (failures, warnings) = QuestionValidation.Validate([question]);

        Assert.Empty(failures);
        var warning = Only(warnings);
        Assert.Equal(rule, warning.Rule);
        Assert.Equal(key, warning.QuestionKey);
    }

    private static void AssertClean(QuestionSpec question)
    {
        var (failures, warnings) = QuestionValidation.Validate([question]);

        Assert.Empty(failures);
        Assert.Empty(warnings);
    }

    private static QuestionSpec Noul(string key, JevContent instructions)
        => new() { Key = key, Kind = QuestionKind.Noul, Instructions = instructions, Options = [] };

    private static QuestionSpec Choice(string key, JevContent instructions, params OptionSpec[] options)
        => new() { Key = key, Kind = QuestionKind.Choice, Instructions = instructions, Options = options };

    private static QuestionSpec Score(string key, JevContent instructions, params OptionSpec[] options)
        => new() { Key = key, Kind = QuestionKind.Score, Instructions = instructions, Options = options };

    private static OptionSpec Option(string key, JevCriterion? criterion) => new(key, key, criterion, -1);

    // An enum Score over members named in ForChoice order, which is declaration order, given the levels at the
    // members' indexes, in call order.
    private static QuestionSpec EnumScore(string key, string[] members, params int[] given)
        => new()
        {
            Key = key,
            Kind = QuestionKind.Score,
            Instructions = "How urgent?",
            EnumMembers = members,
            Options = [.. given.Select((member, level) =>
                new OptionSpec(level.ToString(CultureInfo.InvariantCulture), members[member], "Level", member))],
        };

    private static JevQuestionFailure OnlyFailure(QuestionSpec question)
    {
        var (failures, warnings) = QuestionValidation.Validate([question]);

        Assert.Empty(warnings);
        return Only(failures);
    }

    private static OptionSpec[] Levels(int count)
        => [.. Enumerable.Range(0, count).Select(i => Option(i.ToString(CultureInfo.InvariantCulture), "Level"))];

    private static JevContent Nested(int depth)
        => JevContent.FromUtf8Json(Encoding.UTF8.GetBytes(new string('[', depth) + new string(']', depth)));

    private static JevQuestionFailure Only(JevQuestionFailure[] items)
    {
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        return Assert.Single(items);
#pragma warning restore HLQ005
    }
}
