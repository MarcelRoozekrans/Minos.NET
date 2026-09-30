namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// An enum the builder reads from its public fields under Native AOT: declared out of value order, with an alias
/// declared after its original, so the smoke app checks the published build keeps the fields, keeps their
/// declaration order and keys the aliased value by its first declared name.
/// </summary>
public enum Channel
{
    Chat = 1,
    Email = 0,
    Phone = 2,

    // CA1069 false positive: a deliberate alias declared after the member it repeats, test data for the builder's alias rule.
#pragma warning disable CA1069
    Mail = Email,
#pragma warning restore CA1069
}

/// <summary>A question set built at run time, with enum and keyed questions, published with Native AOT.</summary>
internal static class SmokeBuiltSet
{
    /// <summary>The questions <see cref="AliasedChoice"/> must send: declaration order, <c>Mail</c> skipped.</summary>
    public const string AliasedChoiceQuestions = """{"channel":{"type":"choice","instructions":"Which channel is `message` from?","criteria":{"chat":null,"email":null,"phone":null}}}""";

    public const string AliasedChoiceResponseJson = """{"model":"jev-1.13.0","answers":{"channel":{"type":"choice","choice":"email","probabilities":{"chat":0.1,"email":0.8,"phone":0.1},"confidence":0.75}},"usage":{"input_tokens":120,"output_tokens":8}}""";

    /// <summary>One enum Choice over <see cref="Channel"/>, none of its options described.</summary>
    public static JevQuestionSet AliasedChoice(out ChoiceHandle<Channel> channel)
    {
        var built = JevQuestionSet.CreateBuilder().Choice<Channel>("channel", "Which channel is `message` from?", out channel).Build();
        return built.IsSuccess ? built.Value : throw new InvalidOperationException("The aliased smoke question set is invalid: " + built.Error.Message);
    }

    public const string ResponseJson = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"product":{"type":"choice","choice":"pro-plan","probabilities":{"pro-plan":0.9,"team-plan":0.1},"confidence":0.9},"urgency":{"type":"score","score":1.9,"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    /// <summary>A three-question builder: a Noul, an enum Choice and a keyed Choice.</summary>
    public static JevQuestionSetBuilder Builder(out NoulHandle credentials, out ChoiceHandle<Team> team, out KeyedChoiceHandle product)
        => JevQuestionSet.CreateBuilder()
            .Noul("requests_credentials", "Does `message` ask for a credential?", out credentials)
            .Choice<Team>("team", "Which team should handle `message`?", out team, o => o
                .Describe(Team.Billing, JevCriterion.Text("Charges, invoices, refunds").WithExamples("I was charged twice"))
                .Describe(Team.Account, "Login, profile, permissions"))
            .Choice("product", "Which product is `message` about?", out product, o => o
                .Option("pro-plan", "The Pro subscription")
                .Option("team-plan", "The Team subscription"));

    /// <summary>The three questions plus an enum Score, built.</summary>
    public static JevQuestionSet Full(
        out NoulHandle credentials, out ChoiceHandle<Team> team, out KeyedChoiceHandle product, out ScoreHandle<Urgency> urgency)
    {
        var built = Builder(out credentials, out team, out product)
            .Score<Urgency>("urgency", "How urgent is `message`?", out urgency, l => l
                .Level(Urgency.Low, "Can wait")
                .Level(Urgency.Medium, "This week")
                .Level(Urgency.High, "Today"))
            .Build();
        return built.IsSuccess ? built.Value : throw new InvalidOperationException("The smoke question set is invalid: " + built.Error.Message);
    }
}
