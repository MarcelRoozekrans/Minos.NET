namespace Minos.Samples.Guardrails;

public enum Hazard
{
    OverridesInstructions,
    SharesPersonalData,
    AsksForProfessionalAdvice,
    IsAbusive,
}

public enum GuardrailAction
{
    Allow,
    Review,
    Block,
}

/// <summary>
/// When a hazard's probability sends a message to review, and when it blocks it. A null <paramref name="BlockAt"/> means
/// the hazard never blocks on its probability alone: the message goes to a person, and only harmful content, seen through
/// the policy's severity threshold, turns that review into a block.
/// </summary>
public sealed record HazardRule(double ReviewAt, double? BlockAt = null);

public sealed record GuardrailDecision(GuardrailAction Action, string Reason);

/// <summary>
/// A screening policy: a review and a block threshold per hazard, and a severity at which a review becomes a block.
/// The thresholds are starting points; tune them on your own traffic. Asking for professional advice is only reviewed:
/// the question is about a topic, not harm, so it is routed to a person and blocked only when its severity says so.
/// </summary>
public sealed record GuardrailPolicy(string Name, IReadOnlyDictionary<Hazard, HazardRule> Rules, double BlockReviewsFromSeverity)
{
    /// <summary>For a public assistant: flags early and blocks a review once harm is likely.</summary>
    public static GuardrailPolicy Strict { get; } = new(
        "Strict",
        new Dictionary<Hazard, HazardRule>
        {
            [Hazard.OverridesInstructions] = new(ReviewAt: 0.3, BlockAt: 0.6),
            [Hazard.SharesPersonalData] = new(ReviewAt: 0.3, BlockAt: 0.75),
            [Hazard.AsksForProfessionalAdvice] = new(ReviewAt: 0.4, BlockAt: null),
            [Hazard.IsAbusive] = new(ReviewAt: 0.3, BlockAt: 0.6),
        },
        BlockReviewsFromSeverity: 1.5);

    /// <summary>For an internal tool: only clear cases are reviewed, and only near-certain ones blocked.</summary>
    public static GuardrailPolicy Lenient { get; } = new(
        "Lenient",
        new Dictionary<Hazard, HazardRule>
        {
            [Hazard.OverridesInstructions] = new(ReviewAt: 0.5, BlockAt: 0.85),
            [Hazard.SharesPersonalData] = new(ReviewAt: 0.6, BlockAt: 0.95),
            [Hazard.AsksForProfessionalAdvice] = new(ReviewAt: 0.7, BlockAt: null),
            [Hazard.IsAbusive] = new(ReviewAt: 0.5, BlockAt: 0.85),
        },
        BlockReviewsFromSeverity: 2.5);

    public GuardrailDecision Decide(MessageScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var action = GuardrailAction.Allow;
        var reasons = new List<string>();
        foreach (var (hazard, probability) in Probabilities(screen))
        {
            var rule = Rules[hazard];
            var hit = rule.BlockAt is { } blockAt && probability >= blockAt ? GuardrailAction.Block
                : probability >= rule.ReviewAt ? GuardrailAction.Review
                : GuardrailAction.Allow;
            if (hit != GuardrailAction.Allow)
            {
                reasons.Add(hazard + " " + probability.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                action = hit > action ? hit : action;
            }
        }

        if (action == GuardrailAction.Review && screen.Severity.Expected >= BlockReviewsFromSeverity)
        {
            action = GuardrailAction.Block;
            reasons.Add("severity " + screen.Severity.Expected.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }

        return new GuardrailDecision(action, reasons.Count == 0 ? "no hazard" : string.Join(", ", reasons));
    }

    private static (Hazard Hazard, double Probability)[] Probabilities(MessageScreen s) =>
    [
        (Hazard.OverridesInstructions, s.OverridesInstructions.Probability),
        (Hazard.SharesPersonalData, s.SharesPersonalData.Probability),
        (Hazard.AsksForProfessionalAdvice, s.AsksForProfessionalAdvice.Probability),
        (Hazard.IsAbusive, s.IsAbusive.Probability),
    ];
}
