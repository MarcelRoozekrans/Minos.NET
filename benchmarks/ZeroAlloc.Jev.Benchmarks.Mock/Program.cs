using ZeroAlloc.Jev.Benchmarks.Mock;

var port = 5005;
for (var i = 0; i < args.Length - 1; i++)
{
    if (string.Equals(args[i], "--port", StringComparison.Ordinal))
    {
        port = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
    }
}

using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

var server = MockHost.Start(port, Path.Combine(AppContext.BaseDirectory, "response.json"));
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
server.Stop();
server.Dispose();
