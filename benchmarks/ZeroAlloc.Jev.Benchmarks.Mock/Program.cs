using ZeroAlloc.Jev.Benchmarks.Mock;
using ZeroAlloc.Jev.Benchmarks.Shared;

var port = 5005;
for (var i = 0; i < args.Length - 1; i++)
{
    if (string.Equals(args[i], "--port", StringComparison.Ordinal))
    {
        port = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
    }
    else if (string.Equals(args[i], "--cores", StringComparison.Ordinal))
    {
        // Pin before Kestrel starts, so every thread the server creates runs on these cores.
        try
        {
            CoreAffinity.Apply(CoreAffinity.Parse(args[i + 1]));
        }
        catch (Exception e) when (e is ArgumentException or PlatformNotSupportedException)
        {
            await Console.Error.WriteLineAsync(e.Message).ConfigureAwait(false);
            return 2;
        }
    }
}

using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

var server = await MockHost.StartAsync(port, Path.Combine(AppContext.BaseDirectory, "response.json"), CancellationToken.None).ConfigureAwait(false);
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
