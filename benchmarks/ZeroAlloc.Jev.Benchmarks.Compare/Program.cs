using ZeroAlloc.Jev.Benchmarks.Compare;

CompareOptions options;
try
{
    options = CompareOptions.Parse(args, Environment.MachineName);
}
catch (ArgumentException e)
{
    await Console.Error.WriteLineAsync(e.Message).ConfigureAwait(false);
    await Console.Error.WriteLineAsync(CompareOptions.Usage).ConfigureAwait(false);
    return 2;
}

try
{
    var path = await new Harness(options, Console.Out).RunAsync(CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine("Wrote " + path);
    return 0;
}
catch (InvalidOperationException e)
{
    await Console.Error.WriteLineAsync(e.Message).ConfigureAwait(false);
    return 1;
}
catch (HttpRequestException e)
{
    await Console.Error.WriteLineAsync("A request to the mock failed: " + e.Message).ConfigureAwait(false);
    return 1;
}
