namespace ZeroAlloc.Jev;

/// <summary>The models and aliases available to the account.</summary>
public sealed class ModelList
{
    /// <summary>Gets one entry per model or alias.</summary>
    public required IReadOnlyList<ModelCard> Models { get; init; }
}
