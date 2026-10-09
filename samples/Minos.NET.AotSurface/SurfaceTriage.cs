namespace Minos.AotSurface;

public enum SurfaceTeam
{
    [Criteria("Charges, invoices, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Login, profile, permissions")]
    Account,
}

public enum SurfaceUrgency
{
    [Level("Can wait")]
    Low,

    [Level("This week")]
    Medium,

    [Level("Today")]
    High,
}

/// <summary>One question set of each kind, so ILC analyses the code the generator emits as well as the packages.</summary>
[JevQuestions]
public partial record SurfaceTriage
{
    [Noul("Does `message` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }

    [Choice("Which team should handle `message`?")]
    public partial Choice<SurfaceTeam> Team { get; }

    [Score("How urgent is `message`?")]
    public partial Score<SurfaceUrgency> Urgency { get; }
}
