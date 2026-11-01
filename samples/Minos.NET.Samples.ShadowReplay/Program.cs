using System.Runtime.InteropServices;
using Minos.Samples.ShadowReplay;

// The first Ctrl+C or SIGTERM cancels the replay, which flushes and keeps the records it has written. A second one is
// left to the default handling, which ends the process at once, as `minos calibrate` does.
using var stop = new CancellationTokenSource();
var signalled = 0;

void OnSignal(Action<bool> cancel)
{
    if (Interlocked.Exchange(ref signalled, 1) != 0)
    {
        return;
    }

    cancel(true);
    try
    {
        stop.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // The replay has already finished.
    }
}

Console.CancelKeyPress += (_, e) => OnSignal(cancel => e.Cancel = cancel);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => OnSignal(cancel => context.Cancel = cancel));

return await ShadowReplayApp.RunAsync(args, Console.Out, Console.Error, stop.Token).ConfigureAwait(false);
