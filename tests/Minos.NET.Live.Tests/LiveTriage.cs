namespace Minos.Live.Tests;

public enum Team
{
    [Criterion("Payments, invoicing, refunds")]
    Billing,

    [Criterion("Bugs, outages, integrations")]
    Technical,

    [Criterion("Pricing, upgrades, new accounts")]
    Sales,
}

public enum Frustration
{
    [Level("Calm")]
    Calm,

    [Level("Frustrated")]
    Frustrated,

    [Level("Angry")]
    Angry,
}

/// <summary>
/// The same triage questions as <see cref="Live.Request"/>, declared with <c>[Questions]</c> so the generated
/// question set and typed answer parser can be exercised against a real response.
/// </summary>
[Questions]
public partial record LiveTriage
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which team should handle this?")]
    public partial Choice<Team> Team { get; }

    [Score("How frustrated is the customer?")]
    public partial Score<Frustration> Frustration { get; }
}
