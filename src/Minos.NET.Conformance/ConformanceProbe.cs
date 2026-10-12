namespace Minos.Conformance;

/// <summary>What a probe expects back.</summary>
public enum ProbeKind
{
    /// <summary>Answers to every question.</summary>
    Answer,

    /// <summary>A 400 or 422: the request is one the provider must reject.</summary>
    Invalid,

    /// <summary>A 401 or 403: the request is sent with an invalid key.</summary>
    BadKey,
}

/// <summary>
/// One fixed request the suite sends, with a stable id. The probes ask only what every provider can express: no Noul
/// <c>WhenTrue</c> or <c>WhenFalse</c>, which OpenAI rejects as MIN204, and no criterion examples, which it rejects as MIN205.
/// </summary>
public sealed class ConformanceProbe
{
    /// <summary>The state every probe asks about.</summary>
    public const string State = "Help! My payouts have been failing for 3 days.";

    private ConformanceProbe(string id, ProbeKind kind, QuestionSetDefinition definition)
    {
        Id = id;
        Kind = kind;
        Request = new DecisionRequest(definition, State);
    }

    /// <summary>Gets the probe's id, by which a fixture stores its exchange.</summary>
    public string Id { get; }

    /// <summary>Gets what the probe expects back.</summary>
    public ProbeKind Kind { get; }

    /// <summary>Gets the request, without a model; the runner sets the target's.</summary>
    public DecisionRequest Request { get; }

    /// <summary>Gets the questions the probe asks.</summary>
    public QuestionSetDefinition Definition => Request.Definition;

    /// <summary>Gets one Noul question, <c>is_urgent</c>.</summary>
    public static ConformanceProbe Noul { get; } = new("noul", ProbeKind.Answer, new QuestionSetDefinition(Urgent()));

    /// <summary>Gets one Choice question, <c>department</c>, with the options <c>billing</c> and <c>technical</c>.</summary>
    public static ConformanceProbe Choice { get; } = new("choice", ProbeKind.Answer, new QuestionSetDefinition(Department()));

    /// <summary>Gets one Score question, <c>frustration</c>, with three levels.</summary>
    public static ConformanceProbe Score { get; } = new("score", ProbeKind.Answer, new QuestionSetDefinition(Frustration()));

    /// <summary>
    /// Gets a Noul, a Choice and a Score in one set, keyed <c>is_urgent</c>, <c>department</c> and <c>frustration</c>: not
    /// alphabetical, so an answer matched by position instead of by key shows.
    /// </summary>
    public static ConformanceProbe Mixed { get; } = new("mixed", ProbeKind.Answer, new QuestionSetDefinition(Urgent(), Department(), Frustration()));

    /// <summary>Gets a set built with <see cref="QuestionSetBuilder"/>: a Choice keyed <c>pro-plan</c> and <c>team-plan</c>, and a keyed Score.</summary>
    public static ConformanceProbe Keyed { get; } = new("keyed", ProbeKind.Answer, KeyedDefinition());

    /// <summary>Gets a request with no questions, which every provider must reject with 400 or 422.</summary>
    public static ConformanceProbe Invalid { get; } = new("invalid", ProbeKind.Invalid, new QuestionSetDefinition());

    /// <summary>Gets the <see cref="Noul"/> request sent with an invalid key.</summary>
    public static ConformanceProbe BadKey { get; } = new("bad-key", ProbeKind.BadKey, new QuestionSetDefinition(Urgent()));

    /// <summary>Gets every probe, in the order they run.</summary>
    public static IReadOnlyList<ConformanceProbe> All { get; } = [Noul, Choice, Score, Mixed, Keyed, Invalid, BadKey];

    private static QuestionDefinition Urgent() => QuestionDefinition.Noul("is_urgent", "Does this convey urgency?");

    private static QuestionDefinition Department()
        => QuestionDefinition.Choice(
            "department",
            "Which team should handle this?",
            new OptionDefinition("billing", Criterion.Text("Payments, invoicing, refunds")),
            new OptionDefinition("technical", Criterion.Text("Bugs, outages, integrations")));

    private static QuestionDefinition Frustration()
        => QuestionDefinition.Score(
            "frustration",
            "How frustrated is the customer?",
            Criterion.Text("Calm"),
            Criterion.Text("Frustrated"),
            Criterion.Text("Very angry"));

    private static QuestionSetDefinition KeyedDefinition()
    {
        var built = QuestionSet.CreateBuilder()
            .Choice(
                "product",
                "Which product is this about?",
                out KeyedChoiceHandle _,
                options => options.Option("pro-plan", Criterion.Text("The Pro subscription")).Option("team-plan", Criterion.Text("The Team subscription")))
            .Score(
                "effort",
                "How much effort will this take?",
                out KeyedScoreHandle _,
                levels => levels.Level(Criterion.Text("Minutes")).Level(Criterion.Text("Hours")).Level(Criterion.Text("Days")))
            .Build();

        return built.IsSuccess
            ? built.Value.Definition
            : throw new InvalidOperationException("The keyed probe's set does not build: " + built.Error.Message);
    }
}
