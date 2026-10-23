using System.Runtime.InteropServices;

namespace Minos.Tool;

/// <summary>
/// Turns the signals that stop the tool into a cancellation token. The first signal cancels the token and keeps the
/// process alive, so a cancelled <c>minos calibrate</c> can still save the items it paid for and then exit with 130; it
/// says so on stderr. A signal after that is handled the default way, which ends the process without saving.
/// </summary>
internal sealed class StopSignals : IDisposable
{
    /// <summary>The line the first signal prints.</summary>
    internal const string SavingMessage = "Stopping; press Ctrl+C again to quit at once.";

    private readonly TextWriter _stderr;
    private readonly CancellationTokenSource _cts = new();
    private readonly PosixSignalRegistration[] _registrations;
    private int _signalled;
    private int _disposed;

    /// <summary>
    /// The signals handled. SIGINT is Ctrl+C. SIGQUIT is Ctrl+\ on Unix and Ctrl+Break on Windows. SIGTERM is a request
    /// to terminate on Unix, such as the one a CI step passes on; on Windows it stands for a system shutdown, which only a
    /// service sees, so there only Ctrl+C and Ctrl+Break stop the tool and save.
    /// </summary>
    internal static readonly PosixSignal[] Signals = [PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM];

    /// <summary>Registers the handler for each of <see cref="Signals"/>.</summary>
    /// <param name="stderr">Where the first signal says that the finished items are being saved.</param>
    public StopSignals(TextWriter stderr)
    {
        _stderr = stderr;
        _registrations = [.. Signals.Select(signal => PosixSignalRegistration.Create(signal, Handle))];
    }

    /// <summary>Gets the token that the first signal cancels.</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// Handles a signal. The first one cancels <see cref="Token"/>, keeps the process alive so the command ends on its own,
    /// and prints one line on stderr. A later one, or one after <see cref="Dispose"/>, is left to the default handling.
    /// </summary>
    /// <param name="context">The signal.</param>
    internal void Handle(PosixSignalContext context)
    {
        // A later signal leaves context.Cancel as it is, false by default, so the process ends the default way.
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _signalled, 1) != 0)
        {
            return;
        }

        context.Cancel = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The command has already finished.
        }

        // Cancel first, so a stderr that cannot be written, such as a closed one, never stops the command from saving.
        try
        {
            _stderr.WriteLine(SavingMessage);
        }
        catch (IOException)
        {
            // The line is only a hint; the token is already cancelled.
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
