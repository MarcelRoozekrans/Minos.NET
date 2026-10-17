namespace Minos.Evaluation;

/// <summary>The outcome of one dataset item. Exactly one of <see cref="Answers"/> and <see cref="Error"/> is set.</summary>
/// <param name="Id">The item's id.</param>
/// <param name="Line">The item's line in the dataset file.</param>
/// <param name="LatencyMs">How long the request took, in milliseconds.</param>
/// <param name="Cached">Whether the response came from a cache.</param>
/// <param name="Model">The model that answered, or <see langword="null"/>.</param>
/// <param name="InputTokens">The input token count, or <see langword="null"/>.</param>
/// <param name="OutputTokens">The output token count, or <see langword="null"/>.</param>
/// <param name="Cost">The cost the provider reported, or <see langword="null"/>.</param>
/// <param name="ResponseId">The response id, or <see langword="null"/>.</param>
/// <param name="Answers">The answers by question key, or <see langword="null"/> when the request failed.</param>
/// <param name="Error">The failure, or <see langword="null"/> when the request succeeded.</param>
public sealed record ItemResult(
    string Id,
    int Line,
    double LatencyMs,
    bool Cached,
    string? Model,
    int? InputTokens,
    int? OutputTokens,
    double? Cost,
    string? ResponseId,
    IReadOnlyDictionary<string, AnswerRecord>? Answers,
    ItemError? Error);
