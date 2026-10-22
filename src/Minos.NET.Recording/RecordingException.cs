namespace Minos.Recording;

/// <summary>
/// Thrown by a <see cref="DecisionRecorder"/> when an erasure or a flush could not be confirmed: the sink threw, or the
/// recorder was disposed before the sink ran it. The message is fixed and never holds an erasure key, a decision id or
/// the sink's message; the sink's exception is the inner exception.
/// </summary>
// The standard parameterless and message constructors are left out on purpose: the message is always a fixed one.
#pragma warning disable RCS1194
public sealed class RecordingException : Exception
{
    internal const string SinkFailedMessage = "The decision record sink failed to run the call.";

    internal const string LostMessage = "The decision recorder was disposed before the sink ran the call.";

    /// <summary>Creates the exception for a sink that threw.</summary>
    /// <param name="innerException">The sink's exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerException"/> is <see langword="null"/>.</exception>
    public RecordingException(Exception innerException)
        : base(SinkFailedMessage, innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }

    private RecordingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for an item the recorder's dispose gave up on.</summary>
    internal static RecordingException Lost()
        => new(LostMessage, new TimeoutException("The recorder's flush timed out."));
}
#pragma warning restore RCS1194
