using Minos.Benchmarks.Mock;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

return await MockProgram.RunAsync(
    args, Console.Out, Console.Error, Console.In, Path.Combine(AppContext.BaseDirectory, "response.json"), stop.Token).ConfigureAwait(false);
