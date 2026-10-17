using System.CommandLine;

namespace Minos.Tool;

/// <summary>The minos command line.</summary>
internal static class MinosCli
{
    /// <summary>Runs the tool.</summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="stdout">Where results and written paths go.</param>
    /// <param name="stderr">Where problems go.</param>
    /// <param name="network">Makes the HTTP handler for provider calls; <see langword="null"/> uses the real network.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<HttpMessageHandler>? network = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var root = new RootCommand("Calibrates a decision model against a labelled dataset.");
        root.Subcommands.Add(CalibrateCommand.Create(stdout, stderr, network ?? (() => new SocketsHttpHandler())));

        var parse = root.Parse(args.Length == 0 ? ["--help"] : args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
            {
                await stderr.WriteLineAsync(error.Message).ConfigureAwait(false);
            }

            return ExitCodes.Usage;
        }

        var configuration = new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            EnableDefaultExceptionHandler = false,
            ProcessTerminationTimeout = null,
        };
        return await parse.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }
}
