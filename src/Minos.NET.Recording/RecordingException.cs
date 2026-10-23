namespace Minos.Recording;

/// <summary>
/// Thrown by a <see cref="DecisionRecorder"/> when an erasure or a flush could not be confirmed: the sink threw, or the
/// recorder was disposed before the sink ran it. The recorder's message is fixed and never holds an erasure key, a
/// decision id or the sink's message; the sink's exception is the inner exception.
/// </summary>
public sealed class RecordingException : Exception
{
    internal const string SinkFailedMessage = "The decision record sink failed to run the call.";

    internal const string LostMessage = "The decision recorder was disposed before the sink ran the call.";

    /// <summary>Creates the exception with a default message.</summary>
    public RecordingException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public RecordingException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused it, or <see langword="null"/>.</param>
    public RecordingException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a sink that threw.</summary>
    /// <param name="innerException">The sink's exception.</param>
    internal static RecordingException SinkFailed(Exception innerException)
        => new(SinkFailedMessage, innerException);

    /// <summary>Creates the exception for an item the recorder's dispose gave up on.</summary>
    internal static RecordingException Lost()
        => new(LostMessage, new TimeoutException("The recorder's flush timed out."));
}
