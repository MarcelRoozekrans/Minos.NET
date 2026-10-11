namespace Minos.OpenAI.Protocols;

/// <summary>
/// Thrown by <see cref="OpenAIDecisionsProtocol.ReadAnswers{TResult}"/> when the model refused one or more questions, so
/// the reader can return <see cref="DecisionErrorKind.Refused"/> while the protocol keeps
/// <c>IDecisionProtocol.ReadAnswers</c>'s signature. It never leaves the protocol.
/// </summary>
internal sealed class RefusalException : Exception
{
    /// <summary>Initializes a new instance that names no question.</summary>
    public RefusalException()
        : this([])
    {
    }

    /// <summary>Initializes a new instance with a message that names no question.</summary>
    /// <param name="message">The message.</param>
    public RefusalException(string message)
        : base(message)
    {
        Keys = [];
    }

    /// <summary>Initializes a new instance with a message and an inner exception, naming no question.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception behind this one.</param>
    public RefusalException(string message, Exception innerException)
        : base(message, innerException)
    {
        Keys = [];
    }

    /// <summary>Initializes a new instance naming the refused questions.</summary>
    /// <param name="keys">The refused questions' keys, in answer order.</param>
    public RefusalException(IReadOnlyList<string> keys)
        : base("The model refused to answer: " + string.Join(", ", keys) + ".")
    {
        Keys = keys;
    }

    /// <summary>Gets the refused questions' keys, in answer order.</summary>
    public IReadOnlyList<string> Keys { get; }
}
