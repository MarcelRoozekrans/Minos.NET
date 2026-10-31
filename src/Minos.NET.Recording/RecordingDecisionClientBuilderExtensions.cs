using Minos.Recording;

namespace Minos;

/// <summary>Adds <see cref="RecordingDecisionClient"/> to a pipeline.</summary>
public static class RecordingDecisionClientBuilderExtensions
{
    /// <summary>
    /// Adds a stage that hands a record of each decision to the <see cref="DecisionRecorder"/> of the services passed to
    /// <c>Build</c>, without waiting for it, and returns the inner client's result unchanged.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// As <see cref="UseRecording(DecisionClientBuilder, DecisionRecorder?)"/> with no recorder. <c>Build</c> throws
    /// <see cref="InvalidOperationException"/> when the services have none.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseRecording(this DecisionClientBuilder builder) => UseRecording(builder, recorder: null);

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
    public static DecisionClientBuilder UseRecording(this DecisionClientBuilder builder, DecisionRecorder? recorder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, services) => new RecordingDecisionClient(inner, Resolve(recorder, services)));
    }

    /// <summary>
    /// Adds a stage that hands a record of each decision to a <see cref="DecisionRecorder"/>, without waiting for it, and
    /// runs a candidate client in the background beside each recorded decision, whose answers are written into the
    /// decision's record. It returns the inner client's result unchanged and never waits for the recorder or the candidate.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="shadow">The candidate and its limits. The stage does not dispose the candidate.</param>
    /// <param name="recorder">The recorder; <see langword="null"/> to take the <see cref="DecisionRecorder"/> of the services passed to <c>Build</c>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// Add the stage outermost, as the overload without a candidate: a cache hit is then seen as one and not shadowed, and
    /// a retried call is shadowed once. Each pipeline built keeps its own daily cap and count of running candidates.
    /// </para>
    /// <para>
    /// <c>Build</c> throws <see cref="InvalidOperationException"/> when no recorder is passed and the services have none.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="shadow"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseRecording(this DecisionClientBuilder builder, ShadowOptions shadow, DecisionRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shadow);
        return builder.Use((inner, services) => new RecordingDecisionClient(inner, Resolve(recorder, services), shadow));
    }

    private static DecisionRecorder Resolve(DecisionRecorder? recorder, IServiceProvider services)
        => recorder
            ?? services.GetService(typeof(DecisionRecorder)) as DecisionRecorder
            ?? throw new InvalidOperationException(
                "UseRecording found no DecisionRecorder: register one in the services passed to Build, or pass one to UseRecording.");
}
