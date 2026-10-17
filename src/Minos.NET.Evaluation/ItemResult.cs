namespace Minos.Evaluation;

/// <summary>The outcome of one dataset item. Exactly one of <see cref="Answers"/> and <see cref="Error"/> is set.</summary>
public sealed class ItemResult
{
    /// <summary>Gets the item's id.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the item's line in the dataset file.</summary>
    public required int Line { get; init; }

    /// <summary>Gets how long the request took, in milliseconds.</summary>
    public required double LatencyMs { get; init; }

    /// <summary>Gets whether the response came from a cache.</summary>
    public bool Cached { get; init; }

    /// <summary>Gets the model that answered, or <see langword="null"/>.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the input token count, or <see langword="null"/>.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Gets the output token count, or <see langword="null"/>.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Gets the cost the provider reported, or <see langword="null"/>.</summary>
    public double? Cost { get; init; }

    /// <summary>Gets the response id, or <see langword="null"/>.</summary>
    public string? ResponseId { get; init; }

    /// <summary>Gets the answers by question key, or <see langword="null"/> when the request failed.</summary>
    public IReadOnlyDictionary<string, AnswerRecord>? Answers { get; init; }

    /// <summary>
    /// Gets the failure, or <see langword="null"/> when the request succeeded. An <c>InvalidResponse</c> error keeps the
    /// response's model, token counts and cost, since the request was served and billed.
    /// </summary>
    public ItemError? Error { get; init; }
}
