namespace Minos.Evaluation;

/// <summary>What a run was, saved at the top of its results.</summary>
public sealed class RunHeader
{
    /// <summary>Gets the dataset's name.</summary>
    public required string DatasetName { get; init; }

    /// <summary>Gets the number of items sent.</summary>
    public required int ItemCount { get; init; }

    /// <summary>Gets the client's provider name, or <see langword="null"/> when it reports none.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Gets the client's endpoint, or <see langword="null"/>.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>Gets the client's default model, or <see langword="null"/>.</summary>
    public string? DefaultModel { get; init; }

    /// <summary>Gets when the run started.</summary>
    public required DateTimeOffset Started { get; init; }

    /// <summary>Gets when the run finished.</summary>
    public required DateTimeOffset Finished { get; init; }

    /// <summary>Gets the most requests allowed in flight at once.</summary>
    public required int Parallelism { get; init; }

    /// <summary>
    /// Gets a value that says the run stopped early, so the results hold only the items that finished and
    /// <see cref="ItemCount"/> counts those. It is saved as <c>"partial":true</c> and left out when <see langword="false"/>.
    /// </summary>
    public bool Partial { get; init; }
}
