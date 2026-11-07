using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI.Decisions;

/// <summary>
/// The Minos <see cref="DecisionError"/> behind an exception that <see cref="MinosDecisionClient"/> throws. The adapter
/// throws an upstream <see cref="DecisionClientException"/>, or a <see cref="DecisionProtocolException"/> for
/// <see cref="DecisionErrorKind.InvalidResponse"/>, and sets this exception as its inner exception, so an upstream
/// caller can still read the error's kind, status code, retry delay and request id.
/// </summary>
/// <remarks>
/// Upstream's <see cref="DecisionClientException"/> is sealed, so no Minos type derives from it; the error travels
/// inside it instead. The message is the error's message, and the inner exception is the error's own
/// <see cref="DecisionError.Exception"/>.
/// </remarks>
[Experimental("MEAI001")]
public sealed class MinosDecisionException : Exception
{
    /// <summary>Creates the exception with a default message and no error.</summary>
    public MinosDecisionException()
    {
    }

    /// <summary>Creates the exception with a message and no error.</summary>
    /// <param name="message">The message.</param>
    public MinosDecisionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message, the exception that caused it, and no error.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused it, or <see langword="null"/>.</param>
    public MinosDecisionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a Minos error, with the error's message and its exception as the inner exception.</summary>
    /// <param name="error">The Minos error.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public MinosDecisionException(DecisionError error)
        : base(MessageOf(error), error.Exception)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the Minos error. It is never <see langword="null"/> when <see cref="MinosDecisionClient"/> throws the
    /// exception; it is <see langword="null"/> only when other code makes the exception with a constructor that takes no
    /// error.
    /// </summary>
    public DecisionError? Error { get; }

    // Runs before the base constructor reads error.Exception, so a null error throws ArgumentNullException.
    private static string MessageOf(DecisionError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Message;
    }
}
