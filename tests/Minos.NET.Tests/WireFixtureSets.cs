using Minos;

namespace Minos.Tests.WireFixtureSets;

// Copies of the [Questions] cases in Minos.NET.Generator.Tests/Sources.cs, with every type renamed Wf*. Attribute text,
// keys, WhenTrue, WhenFalse, Examples and NotFor are kept as they are: the escaping is what the fixtures pin.

[Questions]
public partial record WfNoulOnly
{
    [Noul("Does this convey urgency?", WhenTrue = "Explicitly time-sensitive", WhenFalse = "No urgency expressed")]
    public partial Noul IsUrgent { get; }
}

public enum WfDepartment
{
    [Criteria("Payments, invoicing, refunds")] Billing,
    [Criteria("Bugs, outages, integrations", Key = "tech")] Technical,
    Other,
}

[Questions]
public partial record WfChoiceOnly
{
    [Choice("Which team should handle this?")]
    public partial Choice<WfDepartment> Department { get; }
}

public enum WfFrustration
{
    [Level("Calm")] Calm,
    [Level("Frustrated")] Frustrated,
    [Level("Very angry")] VeryAngry,
}

[Questions]
public partial record WfScoreOnly
{
    [Score("How frustrated is the customer?")]
    public partial Score<WfFrustration> Frustration { get; }
}

public enum WfTeam
{
    [Criteria("Charges, invoices, refunds")] Billing = 10,
    [Criteria("Login, profile, permissions, or security")] Account = 20,
    [Criteria("No listed team fits")] Other = 30,
    // Deliberate alias: test data for the alias rule, which the builder and the generator both skip.
#pragma warning disable CA1069
    Legacy = 10,
#pragma warning restore CA1069
}

public enum WfUrgency
{
    [Level("Can wait")] Low,
    [Level("This week")] Medium,
    [Level("Today")] High,
}

[Questions]
internal sealed partial class WfMixed
{
    [Noul("Does `message` ask for a \"credential\"?", WhenFalse = "No credential is requested")]
    public partial Noul RequestsCredentials { get; }

    [Choice("Which team should handle `message`?", Key = "route_to")]
    public partial Choice<WfTeam> Team { get; }

    [Score("How urgent is `message`?")]
    internal partial Score<WfUrgency> Urgency { get; }
}

public enum WfStructuredDepartment
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Bugs, outages, integrations", Examples = new[] { "The API returns 500" })]
    Technical,

    [Criteria("Pricing, upgrades, new accounts", Examples = [], NotFor = [])]
    Sales,

    Other,
}

public enum WfStructuredSeverity
{
    [Level("Cosmetic", NotFor = ["Data loss"])]
    Low,

    [Level("Blocks work")]
    High,
}

[Questions]
public partial record WfStructured
{
    [Choice("Which team should handle this?")]
    public partial Choice<WfStructuredDepartment> Department { get; }

    [Score("How severe is this?")]
    public partial Score<WfStructuredSeverity> Severity { get; }
}

public enum WfVerdict
{
    [Criteria("Approved")] Approved,
    [Criteria("Denied")] @for,
}

[Questions]
public partial record WfKeywordMembers
{
    [Choice("What is the verdict?")]
    public partial Choice<WfVerdict> @class { get; }
}

public sealed record WfTicketContext(string CustomerId);

[Questions(State = typeof(WfTicketContext))]
public partial record WfWithState
{
    [Noul("Is this urgent?")]
    public partial Noul IsUrgent { get; }
}
