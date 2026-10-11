namespace Minos;

/// <summary>Errors that every protocol package reports the same way.</summary>
internal static class DecisionErrors
{
    /// <summary>The message of every <see cref="DecisionErrorKind.Disposed"/> error.</summary>
    internal const string DisposedMessage = "The client was disposed while the request was in flight.";

    /// <summary>The failure for an attempt that the client's disposal tore down or kept from being sent.</summary>
    /// <param name="exception">The exception the teardown caused, or <see langword="null"/> when nothing was sent.</param>
    /// <returns>A <see cref="DecisionErrorKind.Disposed"/> error.</returns>
    internal static DecisionError Disposed(Exception? exception)
        => new(DecisionErrorKind.Disposed, DisposedMessage) { Exception = exception };
}
