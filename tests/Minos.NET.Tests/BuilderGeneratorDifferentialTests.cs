using System.Text;
using Minos.Protocols;

namespace Minos.Tests;

// Declared out of value order, so byte identity also checks that both sides send the options in declaration order.
public enum DiffTeam
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing = 2,

    [Criteria("Login, profile, permissions")]
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
[Questions]
public partial record DiffSet
{
    [Noul(EdgeCases.TrickyInstructions, WhenTrue = "Explicitly time-sensitive", WhenFalse = "No urgency expressed")]
    public partial Noul IsUrgent { get; }

    [Noul("Is `message` a duplicate?")]
    public partial Noul IsDuplicate { get; }

    [Choice("Which team should handle `message`?", Key = "route_to")]
    public partial Choice<DiffTeam> Team { get; }

    [Score("How urgent is `message`?")]
    public partial Score<DiffLevel> UrgencyLevel { get; }
}

/// <summary>
/// The builder and the generator send byte-identical questions: escaping, snake_case keys, skipped aliases. Declared
/// sets are text only, so structured JSON is checked on the builder side alone, in <see cref="QuestionSetBuilderTests"/>.
/// </summary>
public sealed class BuilderGeneratorDifferentialTests
{
    [Fact]
    public void BuiltSet_IsByteIdenticalToTheGeneratedSet()
    {
        var built = Built();

        Assert.Equal(Encoding.ASCII.GetString(SystemOneProtocol.QuestionsUtf8(DiffSet.Definition)), Encoding.ASCII.GetString(SystemOneProtocol.QuestionsUtf8(built.Definition)));
        Assert.True(SystemOneProtocol.QuestionsUtf8(DiffSet.Definition).SequenceEqual(SystemOneProtocol.QuestionsUtf8(built.Definition)));
    }

    [Fact]
    public void BuiltSet_DefinitionMatchesTheGeneratedSet()
    {
        var built = Built().Definition;
        var generated = DiffSet.Definition;

        Assert.Equal(generated.Questions.Select(q => (q.Key, q.Kind, q.Options.Count)), built.Questions.Select(q => (q.Key, q.Kind, q.Options.Count)));
        Assert.Equal(generated.Questions.SelectMany(q => q.Options).Select(o => o.Key), built.Questions.SelectMany(q => q.Options).Select(o => o.Key));
        Assert.True(Minos.Protocols.SystemOneProtocol.QuestionsUtf8(generated).SequenceEqual(Minos.Protocols.SystemOneProtocol.QuestionsUtf8(built)));
    }

    internal static QuestionSet Built()
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("is_urgent", EdgeCases.TrickyInstructions, out _, c => c
                .WhenTrue("Explicitly time-sensitive")
                .WhenFalse("No urgency expressed"))
            .Noul("is_duplicate", "Is `message` a duplicate?", out _)
            .Choice<DiffTeam>("route_to", "Which team should handle `message`?", out _, o => o
                .Describe(DiffTeam.Billing, Criterion.Text("Payments, invoicing, refunds")
                    .WithExamples("I was charged twice")
                    .WithNotFor("How much is Pro?"))
                .Describe(DiffTeam.AccountSecurity, "Login, profile, permissions"))
            .Score<DiffLevel>("urgency_level", "How urgent is `message`?", out _, l => l
                .Level(DiffLevel.Low, "Can wait")
                .Level(DiffLevel.Medium, Criterion.Text("This week").WithExamples("Within five days"))
                .Level(DiffLevel.VeryHigh, "Today"))
            .Build();

        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.ToString() : null);
        return built.Value;
    }
}
