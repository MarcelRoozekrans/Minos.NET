namespace ZeroAlloc.Jev.Live.Tests;

public enum Team
{
    [Criteria("Payments, invoicing, refunds")]
    Billing,

    [Criteria("Bugs, outages, integrations")]
    Technical,

    [Criteria("Pricing, upgrades, new accounts")]
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
/// The same triage questions as <see cref="Live.Request"/>, declared with <c>[JevQuestions]</c> so the generated
/// question set and typed answer parser can be exercised against a real response.
/// </summary>
[JevQuestions]
public partial record LiveTriage
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which team should handle this?")]
    public partial Choice<Team> Team { get; }

    [Score("How frustrated is the customer?")]
    public partial Score<Frustration> Frustration { get; }
}
