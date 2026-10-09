namespace Minos.AotSmoke;

public enum Team
{
    [Criteria("Charges, invoices, refunds")]
    Billing,

    [Criteria("Login, profile, permissions")]
    Account,
}

public enum Urgency
{
    [Level("Can wait")]
    Low,

    [Level("This week")]
    Medium,

    [Level("Today")]
    High,
}

/// <summary>A question set the generator turns into question JSON and a typed parser at compile time.</summary>
[JevQuestions]
public partial record SmokeTriage
{
    [Noul("Does `message` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }

    [Choice("Which team should handle `message`?")]
    public partial Choice<Team> Team { get; }

    [Score("How urgent is `message`?")]
    public partial Score<Urgency> Urgency { get; }
}

public enum SmokeTeam
{
    [Criteria("Charges, invoices, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Login, profile, permissions")]
    Account,
}

/// <summary>Structured criteria, generated at compile time and published with Native AOT.</summary>
[JevQuestions]
public partial record SmokeStructured
{
    [Noul("Does `message` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }

    [Choice("Which team should handle `message`?")]
    public partial Choice<SmokeTeam> Team { get; }
}
