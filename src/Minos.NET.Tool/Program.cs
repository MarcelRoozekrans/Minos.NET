using Minos.Tool;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    try
    {
        cts.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // The run has already finished.
    }
};

return await MinosCli.RunAsync(args, Console.Out, Console.Error, network: null, cts.Token).ConfigureAwait(false);
