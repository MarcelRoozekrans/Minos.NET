namespace Minos;

/// <summary>How <see cref="EscalatingDecisionClient"/> decides which questions to re-ask, and what it names each client in the path.</summary>
/// <remarks>
/// The settings are checked when an <see cref="EscalatingDecisionClient"/> is created from them, which throws
/// <see cref="ArgumentException"/> for one out of range; the stage copies them, so changing them afterwards has no effect.
/// </remarks>
public sealed class EscalationOptions
{
    /// <summary>Gets or sets the primary client's name in <see cref="DecisionResponse.Path"/>. Must not be empty or equal to <see cref="TargetName"/>. Defaults to <c>"primary"</c>.</summary>
    public string PrimaryName { get; set; } = "primary";

    /// <summary>
    /// Gets or sets the target client's name in <see cref="DecisionResponse.Path"/>. Required when the target is given as a
    /// client or a factory; must not be empty or equal to <see cref="PrimaryName"/>.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>
    /// Gets or sets the threshold for a question with no entry in <see cref="Thresholds"/>, from 0 to 1. An answer whose
    /// certainty is below its threshold is re-asked on the target. Defaults to 0.6.
    /// </summary>
    /// <remarks>
    /// Certainty is a Choice's or Score's confidence, or the larger of <c>p</c> and <c>1 - p</c> for a Noul with
    /// probability <c>p</c>. A threshold of 0 never escalates; 1 escalates every answer below certainty 1.
    /// </remarks>
    public double DefaultThreshold { get; set; } = 0.6;

    /// <summary>
    /// Gets the thresholds per question key, from 0 to 1, each overriding <see cref="DefaultThreshold"/> for its question.
    /// Keys compare ordinally; a key that is not in a request's question set is ignored, so one instance can serve several sets.
    /// </summary>
    public IDictionary<string, double> Thresholds => ThresholdMap;

    // The concrete dictionary behind Thresholds, for the stage's lookups without interface dispatch.
    internal Dictionary<string, double> ThresholdMap { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets how long the target is expected to take. When less time than this is left before the deadline, the
    /// unsure questions are not re-asked. Must not be negative. Defaults to <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan ExpectedLatency { get; set; }

    /// <summary>
    /// Gets or sets the time from the stage's entry to the deadline, used when the request has no
    /// <see cref="DecisionRequest.Deadline"/>; <see langword="null"/> for none. Must be positive when set. Defaults to <see langword="null"/>.
    /// </summary>
    public TimeSpan? Budget { get; set; }

    internal EscalationOptions Clone()
    {
        var clone = new EscalationOptions
        {
            PrimaryName = PrimaryName,
            TargetName = TargetName,
            DefaultThreshold = DefaultThreshold,
            ExpectedLatency = ExpectedLatency,
            Budget = Budget,
        };
        foreach (var (key, threshold) in ThresholdMap)
        {
            clone.ThresholdMap[key] = threshold;
        }

        return clone;
    }

    // The messages name the setting only: a threshold's key is a question key, which an exception message does not carry.
    internal void Validate(string paramName)
    {
        if (string.IsNullOrEmpty(PrimaryName))
        {
            throw new ArgumentException("EscalationOptions.PrimaryName must not be null or empty.", paramName);
        }

        if (string.IsNullOrEmpty(TargetName))
        {
            throw new ArgumentException("EscalationOptions.TargetName must not be null or empty.", paramName);
        }

        if (string.Equals(PrimaryName, TargetName, StringComparison.Ordinal))
        {
            throw new ArgumentException("EscalationOptions.PrimaryName and TargetName must differ.", paramName);
        }

        if (!IsThreshold(DefaultThreshold))
        {
            throw new ArgumentException("EscalationOptions.DefaultThreshold must be between 0 and 1.", paramName);
        }

        foreach (var threshold in Thresholds.Values)
        {
            if (!IsThreshold(threshold))
            {
                throw new ArgumentException("Every value in EscalationOptions.Thresholds must be between 0 and 1.", paramName);
            }
        }

        if (ExpectedLatency < TimeSpan.Zero)
        {
            throw new ArgumentException("EscalationOptions.ExpectedLatency must not be negative.", paramName);
        }

        if (Budget is { } budget && budget <= TimeSpan.Zero)
        {
            throw new ArgumentException("EscalationOptions.Budget must be positive when set.", paramName);
        }
    }

    // NaN fails both comparisons, so it is not a threshold.
    private static bool IsThreshold(double value) => value is >= 0 and <= 1;
}
