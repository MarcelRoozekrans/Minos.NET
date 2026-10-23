using System.CommandLine;
using System.CommandLine.Help;

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

        // A plain command named minos, not a RootCommand: a RootCommand takes its name from the host process, which is
        // Minos.NET.Tool under the tool shim, so the usage line would not name the command the user typed.
        // A plain Command also drops RootCommand's [suggest] and [diagram] directives (dotnet-suggest completion), deliberately.
        var root = new Command("minos", "Calibrates a decision model against a labelled dataset, and checks that a provider conforms.");
        root.Options.Add(new HelpOption());
        root.Options.Add(new VersionOption());
        var handler = network ?? (() => new SocketsHttpHandler());
        root.Subcommands.Add(CalibrateCommand.Create(stdout, stderr, handler));
        root.Subcommands.Add(ReportCommand.Create(stdout, stderr));
        root.Subcommands.Add(ConformanceCommand.Create(stdout, stderr, handler));

        var parse = root.Parse(args.Length == 0 ? ["--help"] : args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
            {
                await stderr.WriteLineAsync(error.Message).ConfigureAwait(false);
            }

            return ExitCodes.Usage;
        }

        // System.CommandLine's own Ctrl+C and SIGTERM handling is off: it would end the process once its timeout ran out,
        // cutting short the save of a cancelled run. The host turns those signals into the token instead, see StopSignals.
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
