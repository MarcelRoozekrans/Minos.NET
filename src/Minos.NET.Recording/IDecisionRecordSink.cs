namespace Minos.Recording;

/// <summary>
/// Where a <see cref="DecisionRecorder"/> hands its records, outcomes and erasures. The recorder calls it from its one
/// background writer, one call at a time and in queue order, so a sink needs no locking of its own when only one
/// recorder writes to it. A call that throws is logged with the exception's type name only, counted and skipped. The
/// recorder never disposes a sink: it is the app's.
/// </summary>
public interface IDecisionRecordSink
{
    /// <summary>Keeps a record.</summary>
    /// <param name="record">The record.</param>
    /// <param name="cancellationToken">Cancelled when the recorder's flush on dispose runs out of time.</param>
    /// <returns>A task that completes when the record is kept.</returns>
    ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken);

    /// <summary>Keeps an outcome of a decision.</summary>
    /// <param name="decisionId">The id of the decision the outcome belongs to.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="cancellationToken">Cancelled when the recorder's flush on dispose runs out of time.</param>
    /// <returns>A task that completes when the outcome is kept.</returns>
    ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Erases every record kept so far with the erasure key, and their outcomes.</summary>
    /// <param name="erasureKey">The erasure key.</param>
    /// <param name="cancellationToken">Cancelled when the recorder's flush on dispose runs out of time.</param>
    /// <returns>A task that completes when the erasure is done, or, for an append-only store, recorded.</returns>
    ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken);

    /// <summary>Erases the record of one decision kept so far, and its outcomes.</summary>
    /// <param name="decisionId">The decision id.</param>
    /// <param name="cancellationToken">Cancelled when the recorder's flush on dispose runs out of time.</param>
    /// <returns>A task that completes when the erasure is done, or, for an append-only store, recorded.</returns>
    ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken);
}
