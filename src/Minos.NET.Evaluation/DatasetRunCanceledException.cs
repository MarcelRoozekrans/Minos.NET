namespace Minos.Evaluation;

/// <summary>
/// Thrown by <see cref="DatasetRunner"/> when the caller's cancellation token cancels the run: the run stops, but the
/// items that finished before it are kept here, because their requests were paid for. It is an
/// <see cref="OperationCanceledException"/> whose <see cref="OperationCanceledException.CancellationToken"/> is the
/// caller's token, so code that catches the cancellation keeps working. When the run stops, requests still in flight
/// are cancelled. Those that end cancelled are left out of the partial results, so any cost the provider charged for them
/// is not in the results or the report; a request that returns anyway is kept.
/// </summary>
public sealed class DatasetRunCanceledException : OperationCanceledException
{
    private const string DefaultMessage = "The dataset run was cancelled.";

    /// <summary>Creates the exception with a default message.</summary>
    public DatasetRunCanceledException()
        : this(DefaultMessage)
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public DatasetRunCanceledException(string message)
        : this(message, null)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused it, or <see langword="null"/>.</param>
    public DatasetRunCanceledException(string message, Exception? innerException)
        : this(message, innerException, CancellationToken.None)
    {
    }

    /// <summary>Creates the exception with a default message and a cancellation token.</summary>
    /// <param name="token">The token that cancelled the run.</param>
    public DatasetRunCanceledException(CancellationToken token)
        : this(DefaultMessage, token)
    {
    }

    /// <summary>Creates the exception with a message and a cancellation token.</summary>
    /// <param name="message">The message.</param>
    /// <param name="token">The token that cancelled the run.</param>
    public DatasetRunCanceledException(string message, CancellationToken token)
        : this(message, null, token)
    {
    }

    /// <summary>Creates the exception with a message, the exception that caused it and a cancellation token.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused it, or <see langword="null"/>.</param>
    /// <param name="token">The token that cancelled the run.</param>
    public DatasetRunCanceledException(string message, Exception? innerException, CancellationToken token)
        : base(message, innerException, token)
    {
    }

    /// <summary>Creates the exception for a run that the caller's token cancelled.</summary>
    /// <param name="partialResults">The items that finished, in dataset order, with a header whose <see cref="RunHeader.Partial"/> is set.</param>
    /// <param name="innerException">The client or progress exception that had already stopped the run, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The caller's token that cancelled the run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="partialResults"/> is <see langword="null"/>.</exception>
    public DatasetRunCanceledException(EvaluationResults partialResults, Exception? innerException, CancellationToken cancellationToken)
        : base(Describe(partialResults), innerException, cancellationToken)
    {
        PartialResults = partialResults;
    }

    /// <summary>
    /// Gets the items that finished before the run was cancelled, in dataset order. Their header has
    /// <see cref="RunHeader.Partial"/> set, and its <see cref="RunHeader.ItemCount"/> is the number of finished items.
    /// They can be saved, and a report can be built from them against the full dataset. It is never
    /// <see langword="null"/> when <see cref="DatasetRunner"/> throws the exception; it is <see langword="null"/> only
    /// when other code makes the exception with one of the constructors that take no results.
    /// </summary>
    public EvaluationResults? PartialResults { get; }

    private static string Describe(EvaluationResults partialResults)
    {
        ArgumentNullException.ThrowIfNull(partialResults);
        var count = partialResults.Items.Count;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"The run was cancelled after {count} finished {(count == 1 ? "item" : "items")}.");
    }
}
