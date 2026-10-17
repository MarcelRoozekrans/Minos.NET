namespace Minos.Evaluation;

/// <summary>
/// Thrown by <see cref="DatasetRunner"/> when the client or the progress callback throws: the run stops, but the items
/// that finished before it are kept here, because their requests were paid for.
/// </summary>
// The standard parameterless and message constructors are left out on purpose: every instance must carry its paid results.
#pragma warning disable RCS1194
public sealed class DatasetRunException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="partialResults">The items that finished, in dataset order, with a header whose <see cref="RunHeader.Partial"/> is set.</param>
    /// <param name="innerException">The exception that stopped the run.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DatasetRunException(EvaluationResults partialResults, Exception innerException)
        : base(Describe(partialResults, innerException), innerException)
    {
        PartialResults = partialResults;
    }

    /// <summary>
    /// Gets the items that finished before the run stopped, in dataset order. Their header has <see cref="RunHeader.Partial"/>
    /// set, and its <see cref="RunHeader.ItemCount"/> is the number of finished items. They can be saved, and a report
    /// can be built from them against the full dataset.
    /// </summary>
    public EvaluationResults PartialResults { get; }

    private static string Describe(EvaluationResults partialResults, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(partialResults);
        ArgumentNullException.ThrowIfNull(innerException);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"The run stopped after {partialResults.Items.Count} finished items: {innerException.Message}");
    }
}
#pragma warning restore RCS1194
