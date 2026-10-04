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

    // Deliberate alias: test data for the alias rule, which the builder and the generator both skip.
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

    public const string ResponseJson = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"product":{"type":"choice","choice":"pro-plan","probabilities":{"pro-plan":0.9,"team-plan":0.1},"confidence":0.9},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

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

    /// <summary>JSON instructions and a JSON description, which only a built set sends: declared sets are text only.</summary>
    public static JevQuestionSet Structured()
    {
        var built = JevQuestionSet.CreateBuilder()
            .Noul(
                "requests_credentials",
                JevContent.FromUtf8Json("""{"question":"Does `message` ask for a credential?","policy":{"strict":true}}"""u8),
                out _)
            .Choice<SmokeTeam>("team", "Which team should handle `message`?", out _, o => o
                .Describe(SmokeTeam.Account, JevCriterion.Json(JevContent.FromUtf8Json(
                    """{"description":"Login, profile, permissions","owner":"identity"}"""u8))))
            .Build();
        return built.IsSuccess ? built.Value : throw new InvalidOperationException("The structured smoke question set is invalid: " + built.Error.Message);
    }

    /// <summary>The answer to <see cref="KeyedRisk"/>: a 4-level keyed Score at expected level 2.4.</summary>
    public const string KeyedRiskResponseJson = """{"model":"jev-1.13.0","answers":{"risk":{"type":"score","score":2.4,"legend":{"0":"None","1":"Low","2":"Elevated","3":"Severe"},"probabilities":{"0":0.0,"1":0.1,"2":0.4,"3":0.5},"confidence":0.87}},"usage":{"input_tokens":120,"output_tokens":10}}""";

    /// <summary>One keyed Score with four levels, built.</summary>
    public static JevQuestionSet KeyedRisk(out KeyedScoreHandle risk)
    {
        var built = JevQuestionSet.CreateBuilder()
            .Score("risk", "How risky is `message`?", out risk, l => l
                .Level("None")
                .Level("Low")
                .Level("Elevated")
                .Level("Severe"))
            .Build();
        return built.IsSuccess ? built.Value : throw new InvalidOperationException("The smoke question set is invalid: " + built.Error.Message);
    }
}
