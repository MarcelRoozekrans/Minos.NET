using ZeroAlloc.Jev.Benchmarks.Mock;
using ZeroAlloc.Jev.Benchmarks.Shared;

MockOptions options;
try
{
    options = MockOptions.Parse(args);

    // Pin before Kestrel starts, so every thread the server creates runs on these cores.
    if (options.Cores is { } cores)
    {
        CoreAffinity.Apply(cores);
    }
}
catch (Exception e) when (e is ArgumentException or PlatformNotSupportedException)
{
    await Console.Error.WriteLineAsync(e.Message).ConfigureAwait(false);
    await Console.Error.WriteLineAsync(MockOptions.Usage).ConfigureAwait(false);
    return 2;
}

using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

var server = await MockHost.StartAsync(options.Port, Path.Combine(AppContext.BaseDirectory, "response.json"), CancellationToken.None).ConfigureAwait(false);
await using (server.ConfigureAwait(false))
{
    Console.WriteLine("ready");

    // Stop on Ctrl+C, or when stdin closes, which is how a harness that spawned the host ends it.
    _ = Task.Run(() =>
    {
        while (Console.In.ReadLine() is not null)
        {
        }

        stop.Set();
    });
    stop.Wait();
}

return 0;
