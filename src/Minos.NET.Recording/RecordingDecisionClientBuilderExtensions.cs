using Minos.Recording;

namespace Minos;

/// <summary>Adds <see cref="RecordingDecisionClient"/> to a pipeline.</summary>
public static class RecordingDecisionClientBuilderExtensions
{
    /// <summary>
    /// Adds a stage that hands a record of each decision to a <see cref="DecisionRecorder"/>, without waiting for it, and
    /// returns the inner client's result unchanged.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="recorder">The recorder; <see langword="null"/> to take the <see cref="DecisionRecorder"/> of the services passed to <c>Build</c>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// Add the stage outermost, before <c>UseCaching</c>, <c>UseRetries</c> and the rest: a decision is then one record, a
    /// cache hit is recorded as cached, and a retried call is one record. Every pipeline built with the same recorder
    /// shares its queue and its one writer. Disposing a pipeline never disposes the recorder: the app or its container does.
    /// </para>
    /// <para>
    /// <c>Build</c> throws <see cref="InvalidOperationException"/> when no recorder is passed and the services have none.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseRecording(this DecisionClientBuilder builder, DecisionRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, services) => new RecordingDecisionClient(
            inner,
            recorder
                ?? services.GetService(typeof(DecisionRecorder)) as DecisionRecorder
                ?? throw new InvalidOperationException(
                    "UseRecording found no DecisionRecorder: register one in the services passed to Build, or pass one to UseRecording.")));
    }
}
