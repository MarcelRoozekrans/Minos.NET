namespace Minos.Evaluation;

/// <summary>What a run was, saved at the top of its results.</summary>
/// <param name="DatasetName">The dataset's name.</param>
/// <param name="ItemCount">The number of items sent.</param>
/// <param name="ProviderName">The client's provider name, or <see langword="null"/> when it reports none.</param>
/// <param name="Endpoint">The client's endpoint, or <see langword="null"/>.</param>
/// <param name="DefaultModel">The client's default model, or <see langword="null"/>.</param>
/// <param name="Started">When the run started.</param>
/// <param name="Finished">When the run finished.</param>
/// <param name="Parallelism">The most requests allowed in flight at once.</param>
public sealed record RunHeader(
    string DatasetName,
    int ItemCount,
    string? ProviderName,
    Uri? Endpoint,
    string? DefaultModel,
    DateTimeOffset Started,
    DateTimeOffset Finished,
    int Parallelism);
