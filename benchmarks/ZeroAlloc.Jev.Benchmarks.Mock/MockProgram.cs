using System.Globalization;
using Microsoft.AspNetCore.Connections;
using ZeroAlloc.Jev.Benchmarks.Shared;

namespace ZeroAlloc.Jev.Benchmarks.Mock;

/// <summary>The mock's process: parses the command line, pins the cores, serves until told to stop, and returns the exit code.</summary>
public static class MockProgram
{
    /// <summary>The exit code for a wrong command line or cores this machine cannot pin.</summary>
    public const int UsageExitCode = 2;

    /// <summary>The exit code when the port is already in use.</summary>
    public const int PortInUseExitCode = 3;

    /// <summary>
    /// Runs the mock: prints <c>ready</c> once it serves, then stops when <paramref name="input"/> ends, which is how a
    /// runner that started it with its stdin held open ends it, or when <paramref name="stop"/> is cancelled.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="output">Where <c>ready</c> goes.</param>
    /// <param name="error">Where errors go.</param>
    /// <param name="input">The input whose end stops the mock.</param>
    /// <param name="responsePath">The recorded answer to serve.</param>
    /// <param name="stop">Stops the mock, as Ctrl+C does.</param>
    /// <returns>0 after a normal stop, 2 for a wrong command line, 3 when the port is taken.</returns>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, TextWriter output, TextWriter error, TextReader input, string responsePath, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(input);
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
            await error.WriteLineAsync(e.Message).ConfigureAwait(false);
            await error.WriteLineAsync(MockOptions.Usage).ConfigureAwait(false);
            return UsageExitCode;
        }

        MockServer server;
        try
        {
            server = await MockHost.StartAsync(options.Port, responsePath, CancellationToken.None).ConfigureAwait(false);
        }
        // Kestrel reports a taken port as an IOException wrapping AddressInUseException.
        catch (IOException e) when (e.InnerException is AddressInUseException)
        {
            await error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Port {options.Port} is already in use.")).ConfigureAwait(false);
            return PortInUseExitCode;
        }

        await using (server.ConfigureAwait(false))
        {
            await output.WriteLineAsync("ready").ConfigureAwait(false);
            await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            var inputEnded = Task.Run(async () =>
            {
                while (await input.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is not null)
                {
                }
            }, CancellationToken.None);
            await Task.WhenAny(inputEnded, Task.Delay(Timeout.Infinite, stop)).ConfigureAwait(false);
        }

        return 0;
    }
}
