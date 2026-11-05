namespace Minos.Telemetry;

/// <summary>
/// The escalation stage's telemetry for one call whose primary answer had an unsure question: the values of the
/// <c>minos.escalation.*</c> span tags and of the <c>minos.escalations</c> counter and its tags. The values are counts,
/// the target's client name and fixed reason names, never question content.
/// </summary>
/// <remarks>
/// The stage allocates one only when a question was unsure, and hangs it off the <see cref="DecisionPath"/> it builds,
/// so a response without a path, or with a path no escalation made, carries no field for it. <see cref="Owner"/> is the
/// response the stage built: <see cref="DecisionResponse"/> reports the telemetry for that instance alone, so a copy
/// that shares the path, such as a cached one, reports none.
/// </remarks>
internal sealed class EscalationTelemetry
{
    internal EscalationTelemetry(string targetTag, int escalated, string? skippedTag)
    {
        TargetTag = targetTag;
        EscalatedTag = escalated > 0 ? escalated : null;
        SkippedTag = skippedTag;
    }

    // minos.escalation.escalated: the number of questions the target answered, boxed once here; null when it answered none.
    public object? EscalatedTag { get; }

    // minos.escalation.skipped: "no_time_left" or "target_failed"; null when the target answered.
    public string? SkippedTag { get; }

    // minos.escalation.target: the target's client name.
    public string TargetTag { get; }

    // minos.escalation.reason: why the unsure questions were or were not escalated.
    public string ReasonTag => SkippedTag ?? DecisionTelemetry.ReasonEscalated;

    // Set once by the stage before it returns the response, which no other code has seen by then.
    public DecisionResponse? Owner { get; set; }
}
