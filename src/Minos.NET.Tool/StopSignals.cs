using System.Runtime.InteropServices;

namespace Minos.Tool;

/// <summary>
/// Turns the signals that stop the tool into a cancellation token: Ctrl+C, Ctrl+Break and the SIGTERM that a CI job's
/// timeout or a service manager sends. The signal's default handling, which ends the process, is cancelled, so a cancelled
/// <c>minos calibrate</c> can still save the items it paid for and then exit with 130.
/// </summary>
internal sealed class StopSignals : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly PosixSignalRegistration[] _registrations;
    private int _disposed;

    /// <summary>The signals handled: SIGINT is Ctrl+C, SIGQUIT is Ctrl+Break on Windows, and SIGTERM is a request to terminate.</summary>
    internal static readonly PosixSignal[] Signals = [PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM];

    /// <summary>Registers the handler for each of <see cref="Signals"/>.</summary>
    public StopSignals()
        => _registrations = [.. Signals.Select(signal => PosixSignalRegistration.Create(signal, Handle))];

    /// <summary>Gets the token that the first signal cancels.</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>Handles a signal: cancels <see cref="Token"/> and keeps the process alive, so the command ends on its own.</summary>
    /// <param name="context">The signal.</param>
    internal void Handle(PosixSignalContext context)
    {
        context.Cancel = true;
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The command has already finished.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _cts.Dispose();
    }
}
