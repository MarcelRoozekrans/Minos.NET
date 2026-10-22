namespace Minos.Recording;

/// <summary>
/// What an app does with a decision recorder after the decision: add outcomes, erase records, and flush. Records
/// themselves are queued by the recording stage. Every call goes through the recorder's queue, so it reaches the sink
/// after everything queued before it.
/// </summary>
public interface IDecisionRecorder
{
    /// <summary>
    /// Queues an outcome of a decision. When the queue is full, the call waits for space instead of dropping the outcome.
    /// </summary>
    /// <param name="decisionId">The id of the decision the outcome belongs to.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="cancellationToken">Cancels the wait for queue space.</param>
    /// <returns>A task that completes when the outcome is queued, not when the sink has it.</returns>
    /// <exception cref="ArgumentException"><paramref name="decisionId"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="decisionId"/> or <paramref name="outcome"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The recorder is disposed.</exception>
    ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases every record with the erasure key that was queued before this call. When the queue is full, the call waits for
    /// space instead of dropping the erasure.
    /// </summary>
    /// <param name="erasureKey">The erasure key.</param>
    /// <param name="cancellationToken">Cancels the wait for queue space and for the sink. Once queued, the erasure still runs.</param>
    /// <returns>A task that completes after the sink ran the erasure.</returns>
    /// <exception cref="ArgumentException"><paramref name="erasureKey"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="erasureKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The recorder is disposed.</exception>
    /// <exception cref="RecordingException">The sink threw, or the recorder was disposed before the sink ran the erasure. The message holds no key; the sink's exception, if any, is the inner exception.</exception>
    ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases the record of one decision, queued before this call. When the queue is full, the call waits for space instead
    /// of dropping the erasure.
    /// </summary>
    /// <param name="decisionId">The decision id.</param>
    /// <param name="cancellationToken">Cancels the wait for queue space and for the sink. Once queued, the erasure still runs.</param>
    /// <returns>A task that completes after the sink ran the erasure.</returns>
    /// <exception cref="ArgumentException"><paramref name="decisionId"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="decisionId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The recorder is disposed.</exception>
    /// <exception cref="RecordingException">The sink threw, or the recorder was disposed before the sink ran the erasure. The message holds no id; the sink's exception, if any, is the inner exception.</exception>
    ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until everything queued before this call has been handed to the sink, whether the sink kept it or threw.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the earlier items reached the sink.</returns>
    /// <exception cref="ObjectDisposedException">The recorder is disposed.</exception>
    /// <exception cref="RecordingException">The recorder was disposed before the earlier items reached the sink.</exception>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}
