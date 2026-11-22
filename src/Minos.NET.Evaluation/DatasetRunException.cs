namespace Minos.Evaluation;

/// <summary>
/// Thrown by <see cref="DatasetRunner"/> when the client or the progress callback throws: the run stops, but the items
/// that finished before it are kept here, because their requests were paid for. When the run stops, requests still in
/// flight are cancelled. Those that end cancelled are left out of the partial results, so any cost the provider charged
/// for them is not in the results or the report; a request that returns anyway is kept.
/// </summary>
public sealed class DatasetRunException : Exception
{
    private const string DefaultMessage = "The dataset run stopped.";

    /// <summary>Creates the exception with a default message.</summary>
    public DatasetRunException()
        : this(DefaultMessage)
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public DatasetRunException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused it, or <see langword="null"/>.</param>
    public DatasetRunException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a run that stopped.</summary>
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
    /// can be built from them against the full dataset. It is never <see langword="null"/> when <see cref="DatasetRunner"/>
    /// throws the exception; it is <see langword="null"/> only when other code makes the exception with one of the
    /// constructors that take no results.
    /// </summary>
    public EvaluationResults? PartialResults { get; }

    private static string Describe(EvaluationResults partialResults, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(partialResults);
        ArgumentNullException.ThrowIfNull(innerException);
        var count = partialResults.Items.Count;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"The run stopped after {count} finished {(count == 1 ? "item" : "items")}: {innerException.Message}");
    }
}
