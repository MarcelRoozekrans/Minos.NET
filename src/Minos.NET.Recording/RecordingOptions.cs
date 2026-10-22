namespace Minos.Recording;

/// <summary>Settings for a <see cref="DecisionRecorder"/>, read once, when it is created.</summary>
public sealed class RecordingOptions
{
    // The longest wait a timer takes.
    private static readonly TimeSpan MaxFlushTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>
    /// Gets or sets how many items the queue holds: records, outcomes, erasures and flushes. When it is full, a record is
    /// dropped and counted, and the other items wait for space.
    /// </summary>
    /// <value>Defaults to 1024. At least 1.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int Capacity
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1024;

    /// <summary>
    /// Gets or sets how long <see cref="DecisionRecorder.DisposeAsync"/> waits for the queue to drain, measured on the
    /// recorder's <see cref="TimeProvider"/>. After it, the sink's call is cancelled, and what was not done is lost and logged.
    /// </summary>
    /// <value>Defaults to 5 seconds. More than zero, and at most <see cref="int.MaxValue"/> milliseconds.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero, negative or too large.</exception>
    public TimeSpan FlushTimeout
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxFlushTimeout);
            field = value;
        }
    } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets what the recording stage does with a decision's state before it is recorded: return
    /// <see cref="RedactionResult.Keep"/> with the state as it is or rewritten, or <see cref="RedactionResult.Drop"/> to
    /// keep no record. It runs on the caller's thread after each call, and only changes what is recorded: the provider
    /// already saw the state as the app gave it.
    /// </summary>
    /// <value>
    /// Defaults to <see langword="null"/>, which records the state as it is. A redactor that throws or returns
    /// <see langword="null"/> keeps no record, never fails the decision, and is logged as event 1011 with the exception's
    /// type name only.
    /// </value>
    public Func<DecisionContent, RedactionResult>? Redact { get; set; }
}
