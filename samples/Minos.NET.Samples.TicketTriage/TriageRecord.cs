namespace Minos.Samples.TicketTriage;

/// <summary>One answer to one question: a Noul has a probability, a Choice has a key and a confidence.</summary>
public sealed record TriageAnswer(string Kind, double? Probability, string? ChoiceKey, double? Confidence);

/// <summary>How one question was routed. <paramref name="Reason"/> is the <c>RouteReason</c> name.</summary>
public sealed record TriageQuestion(
    string Key,
    TriageAnswer Local,
    string AnsweredBy,
    string Reason,
    double PrimaryCertainty,
    double? TargetCertainty,
    TriageAnswer Final);

/// <summary>One client call. <paramref name="Status"/> is the <c>CallStatus</c> name.</summary>
public sealed record TriageCall(
    string Client,
    string? Model,
    int? InputTokens,
    int? OutputTokens,
    double? Cost,
    string Status,
    double? LatencyMs);

/// <summary>One triaged item. <paramref name="Error"/> is set only for a failed item, which has empty lists.</summary>
public sealed record TriageRecord(
    string Id,
    double TotalMs,
    string? Error,
    IReadOnlyList<TriageQuestion> Questions,
    IReadOnlyList<TriageCall> Calls);
