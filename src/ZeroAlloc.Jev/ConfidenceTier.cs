namespace ZeroAlloc.Jev;

/// <summary>
/// How confident an answer is, as <see cref="ConfidenceThresholds.Classify(double)"/> places it. The tiers name confidence
/// levels, not actions: what to do at each tier depends on what being wrong costs.
/// </summary>
public enum ConfidenceTier
{
    /// <summary>Below <see cref="ConfidenceThresholds.Medium"/>: hand the decision to a person, ask again or use a fallback.</summary>
    Low,

    /// <summary>At least <see cref="ConfidenceThresholds.Medium"/> but below <see cref="ConfidenceThresholds.High"/>: confirm or flag it.</summary>
    Medium,

    /// <summary>At least <see cref="ConfidenceThresholds.High"/>: confident enough to act on.</summary>
    High,
}
