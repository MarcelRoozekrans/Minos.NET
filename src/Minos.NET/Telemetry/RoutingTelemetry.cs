namespace Minos.Telemetry;

/// <summary>
/// The routing stages' telemetry for one call, in two optional parts. The escalation part holds the values of the
/// <c>minos.escalation.*</c> span tags and of the <c>minos.escalations</c> counter and its tags, for a call whose primary
/// answer had an unsure question. The fallback part holds the values of the <c>minos.fallback.*</c> span tags, for a call
/// a step after step 0 answered. The values are counts, client and step names and fixed reason names, never question content.
/// </summary>
/// <remarks>
/// <para>
/// A routing stage allocates one only when it has a part to report, or carries one from the inner path it takes over, and
/// hangs it off the <see cref="DecisionPath"/> it builds, so a response without a path, or with a path that reports
/// nothing, carries no field for it.
/// </para>
/// <para>
/// <see cref="Owner"/> is the response the stage built: <see cref="DecisionResponse"/> reports the telemetry for that
/// instance alone, so a copy that shares the path, such as a cached one, reports none. A stage that takes over an inner
/// path builds a new holder with the inner holder's parts and its own, and becomes the owner, so the outermost routing
/// stage's response reports both parts once. It carries the inner holder only when the inner response owns it: a cached
/// copy's holder is not carried.
/// </para>
/// <para>
/// The <c>minos.fallbacks</c> counter is not here: the fallback stage emits it itself, through <see cref="FallbackTelemetry"/>,
/// because an exhausted chain has no response to carry it.
/// </para>
/// </remarks>
internal sealed class RoutingTelemetry
{
    private RoutingTelemetry(string? targetTag, object? escalatedTag, string? skippedTag, object? fallbackStepsTag, string? fallbackAnsweredByTag)
    {
        TargetTag = targetTag;
        EscalatedTag = escalatedTag;
        SkippedTag = skippedTag;
        FallbackStepsTag = fallbackStepsTag;
        FallbackAnsweredByTag = fallbackAnsweredByTag;
    }

    // The escalation part. TargetTag is set when the part is present; the other two are null when they do not apply.

    // minos.escalation.target: the target's client name; null without an escalation part.
    public string? TargetTag { get; }

    // minos.escalation.escalated: the number of questions the target answered, boxed once here; null when it answered none.
    public object? EscalatedTag { get; }

    // minos.escalation.skipped: "no_time_left" or "target_failed"; null when the target answered.
    public string? SkippedTag { get; }

    // minos.escalation.reason: why the unsure questions were or were not escalated; null without an escalation part.
    public string? ReasonTag => TargetTag is null ? null : SkippedTag ?? DecisionTelemetry.ReasonEscalated;

    // The fallback part, both set or both null.

    // minos.fallback.steps: the number of steps after step 0 tried or skipped, the answering one included, boxed once here.
    public object? FallbackStepsTag { get; }

    // minos.fallback.answered_by: the answering step's name.
    public string? FallbackAnsweredByTag { get; }

    // Set once by the stage before it returns the response, which no other code has seen by then.
    public DecisionResponse? Owner { get; set; }

    /// <summary>The escalation stage's holder: its own part, and the fallback part of <paramref name="inner"/>, when there is one.</summary>
    /// <param name="targetTag">The target's client name.</param>
    /// <param name="escalated">The number of questions the target answered.</param>
    /// <param name="skippedTag">Why the target answered none, or <see langword="null"/>.</param>
    /// <param name="inner">The holder the taken-over inner response owns, or <see langword="null"/>.</param>
    internal static RoutingTelemetry Escalation(string targetTag, int escalated, string? skippedTag, RoutingTelemetry? inner)
        => new(targetTag, escalated > 0 ? escalated : null, skippedTag, inner?.FallbackStepsTag, inner?.FallbackAnsweredByTag);

    /// <summary>The fallback stage's holder: its own part, and the escalation part of <paramref name="inner"/>, when there is one.</summary>
    /// <param name="steps">The number of steps after step 0 tried or skipped, the answering one included.</param>
    /// <param name="answeredBy">The answering step's name.</param>
    /// <param name="inner">The holder the taken-over step response owns, or <see langword="null"/>.</param>
    internal static RoutingTelemetry Fallback(int steps, string answeredBy, RoutingTelemetry? inner)
        => new(inner?.TargetTag, inner?.EscalatedTag, inner?.SkippedTag, steps, answeredBy);

    /// <summary>A holder with <paramref name="inner"/>'s parts, for a stage that takes over a path and has no part of its own.</summary>
    /// <param name="inner">The holder the taken-over inner response owns, or <see langword="null"/>.</param>
    /// <returns>A new holder, or <see langword="null"/> when there is nothing to carry.</returns>
    internal static RoutingTelemetry? Carried(RoutingTelemetry? inner)
        => inner is null ? null : new(inner.TargetTag, inner.EscalatedTag, inner.SkippedTag, inner.FallbackStepsTag, inner.FallbackAnsweredByTag);
}
