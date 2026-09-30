using System.Text;

namespace ZeroAlloc.Jev.Tests;

// Declared out of value order, so byte identity also checks that both sides send the options in declaration order.
public enum DiffTeam
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing = 2,

    [Criteria("""{"description":"Login, profile, permissions","owner":"identity"}""", Json = true)]
    AccountSecurity = 0,

    ServiceDesk = 1,

    // Deliberate alias: test data for the alias rule, which the
    // builder and the generator both skip.
#pragma warning disable CA1069
    Helpdesk = ServiceDesk,
#pragma warning restore CA1069
}

public enum DiffLevel
{
    [Level("Can wait")]
    Low,

    [Level("This week", Examples = ["Within five days"])]
    Medium,

    [Level("Today")]
    VeryHigh,
}

/// <summary>The same questions as <see cref="BuilderGeneratorDifferentialTests.Built"/>, declared for the generator.</summary>
[JevQuestions]
public partial record DiffSet
{
    [Noul(EdgeCases.TrickyInstructions, True = "Explicitly time-sensitive", False = "No urgency expressed")]
    public partial Noul IsUrgent { get; }

    [Noul("""{ "question": "Is `message` a duplicate?", "policy": { "strict": true, "limit": 1.50 } }""", Json = true)]
    public partial Noul IsDuplicate { get; }

    [Choice("Which team should handle `message`?", Key = "route_to")]
    public partial Choice<DiffTeam> Team { get; }

    [Score("How urgent is `message`?")]
    public partial Score<DiffLevel> UrgencyLevel { get; }
}

/// <summary>The builder and the generator send byte-identical questions: escaping, snake_case keys, skipped aliases.</summary>
public sealed class BuilderGeneratorDifferentialTests
{
    [Fact]
    public void BuiltSet_IsByteIdenticalToTheGeneratedSet()
    {
        var built = Built();

        Assert.Equal(Encoding.ASCII.GetString(DiffSet.QuestionsUtf8), Encoding.ASCII.GetString(built.QuestionsUtf8));
        Assert.True(DiffSet.QuestionsUtf8.SequenceEqual(built.QuestionsUtf8));
    }

    internal static JevQuestionSet Built()
    {
        var built = JevQuestionSet.CreateBuilder()
            .Noul("is_urgent", EdgeCases.TrickyInstructions, out _, c => c
                .WhenTrue("Explicitly time-sensitive")
                .WhenFalse("No urgency expressed"))
            .Noul("is_duplicate", JevContent.FromUtf8Json(
                """{ "question": "Is `message` a duplicate?", "policy": { "strict": true, "limit": 1.50 } }"""u8), out _)
            .Choice<DiffTeam>("route_to", "Which team should handle `message`?", out _, o => o
                .Describe(DiffTeam.Billing, JevCriterion.Text("Payments, invoicing, refunds")
                    .WithExamples("I was charged twice")
                    .WithNotFor("How much is Pro?"))
                .Describe(DiffTeam.AccountSecurity, JevCriterion.Json(JevContent.FromUtf8Json(
                    """{"description":"Login, profile, permissions","owner":"identity"}"""u8))))
            .Score<DiffLevel>("urgency_level", "How urgent is `message`?", out _, l => l
                .Level(DiffLevel.Low, "Can wait")
                .Level(DiffLevel.Medium, JevCriterion.Text("This week").WithExamples("Within five days"))
                .Level(DiffLevel.VeryHigh, "Today"))
            .Build();

        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.ToString() : null);
        return built.Value;
    }
}
