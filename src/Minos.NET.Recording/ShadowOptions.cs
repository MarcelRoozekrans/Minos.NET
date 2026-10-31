namespace Minos.Recording;

/// <summary>
/// Settings for shadow mode in a <see cref="RecordingDecisionClient"/>: a candidate client that answers each recorded
/// decision a second time, in the background, so its answers can be compared with the primary's. Read when the stage
/// is created; the stage never disposes the candidate.
/// </summary>
public sealed class ShadowOptions
{
    // The longest wait a timer takes.
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>Initializes a new instance of the <see cref="ShadowOptions"/> class.</summary>
    /// <param name="candidate">The client to shadow the primary with. It must not record its own decisions.</param>
    /// <param name="dailyCap">
    /// How many candidate calls may start on one UTC day, measured on the recorder's <see cref="TimeProvider"/>. A call
    /// that errs or times out still counts. <see cref="int.MaxValue"/> sets no cap. Every stage built with these options
    /// shares the cap: share one <see cref="ShadowOptions"/> among the pipelines that shadow the same candidate, so they
    /// share its cap.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dailyCap"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="candidate"/> has a recording stage: its own records would duplicate the decisions in the sink.
    /// </exception>
    public ShadowOptions(IDecisionClient candidate, int dailyCap)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfLessThan(dailyCap, 1);
        if (candidate.GetService<IDecisionRecorder>() is not null)
        {
            throw new ArgumentException("The candidate records its own decisions: shadow a client without UseRecording.", nameof(candidate));
        }

        Candidate = candidate;
        DailyCap = dailyCap;
        Budget = new ShadowBudget(dailyCap);
    }

    /// <summary>Gets the client that shadows the primary.</summary>
    public IDecisionClient Candidate { get; }

    /// <summary>
    /// Gets how many candidate calls may start on one UTC day, among every stage built with these options: share one
    /// <see cref="ShadowOptions"/> among the pipelines that shadow the same candidate, so they share its cap.
    /// </summary>
    public int DailyCap { get; }

    /// <summary>
    /// Gets or sets how long a candidate call may take, measured on the recorder's <see cref="TimeProvider"/>. A call that
    /// takes longer is cancelled and recorded with <see cref="DecisionErrorKind.Timeout"/>.
    /// </summary>
    /// <value>
    /// Defaults to 30 seconds. More than zero, and at most <see cref="int.MaxValue"/> milliseconds, so never
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero, negative, infinite or too large.</exception>
    public TimeSpan Timeout
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxTimeout);
            field = value;
        }
    } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how many candidate calls may run at once, among every stage built with these options. A decision made
    /// while that many run is not shadowed, and counted as <c>skipped_busy</c>. A stage reads it when it is created.
    /// </summary>
    /// <remarks>
    /// A call counts until it returns or reaches <see cref="Timeout"/>, or the recorder is disposed. The stage then stops
    /// waiting for it, but a candidate that ignores its cancellation token can keep running beyond the limit.
    /// </remarks>
    /// <value>Defaults to 4. At least 1.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxConcurrent
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 4;

    /// <summary>
    /// Gets or sets the model to ask the candidate for. It is never the primary's <see cref="DecisionRequest.Model"/>,
    /// which names a model of another provider.
    /// </summary>
    /// <value>Defaults to <see langword="null"/>, which is the candidate's default model.</value>
    public string? Model { get; set; }

    /// <summary>Gets the cap and the running candidates every stage built with these options shares.</summary>
    internal ShadowBudget Budget { get; }
}
