namespace Minos.Evaluation;

/// <summary>The raw outcome of running a dataset: a header and one result per item, in dataset order.</summary>
public sealed class EvaluationResults
{
    /// <summary>Creates results.</summary>
    /// <param name="header">What the run was.</param>
    /// <param name="items">The item results, in dataset order.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public EvaluationResults(RunHeader header, IReadOnlyList<ItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(items);
        Header = header;
        Items = items;
    }

    /// <summary>Gets what the run was.</summary>
    public RunHeader Header { get; }

    /// <summary>Gets the item results, in dataset order.</summary>
    public IReadOnlyList<ItemResult> Items { get; }
}
