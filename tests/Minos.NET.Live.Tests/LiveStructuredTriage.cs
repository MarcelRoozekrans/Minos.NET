namespace Minos.Live.Tests;

public enum StructuredTeam
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Bugs, outages, integrations", Examples = ["The API returns 500"], NotFor = ["Is the API down for everyone?"])]
    Technical,

    [Criteria("Pricing, upgrades, new accounts")]
    Sales,
}

/// <summary>
/// A routing question whose <see cref="StructuredTeam"/> criteria carry <c>Examples</c> and <c>NotFor</c> on more
/// than one option, declared with <c>[Questions]</c> so the structured criterion objects can be exercised
/// against a real response.
/// </summary>
[Questions]
public partial record LiveStructuredRouting
{
    [Choice("Which team should handle this?")]
    public partial Choice<StructuredTeam> Team { get; }
}
