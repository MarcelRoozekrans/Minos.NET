using System.Diagnostics;
using System.Text;

namespace Minos.PackTests;

/// <summary>
/// Runs <c>dotnet</c> with its output captured, safely. A build started by the child must not leave MSBuild worker nodes
/// or the compiler server holding the redirected pipes, or reading them never ends (#145). So node reuse and the shared
/// compiler are off for the child (as environment and, for <c>build</c>, <c>pack</c>, <c>msbuild</c> and <c>restore</c>,
/// as arguments), and both streams are read at the same time so neither pipe can fill and block the other. One time limit
/// bounds the process and the reading of both streams: past it, or if a stream stays open after the process exits, the
/// process tree is killed and a <see cref="TimeoutException"/> carries the command and the output read so far.
/// </summary>
internal static class DotnetProcess
{
    private static readonly TimeSpan DrainWait = TimeSpan.FromSeconds(5);

    public static (int ExitCode, string Output, string Error) Run(string workingDirectory, TimeSpan timeout, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (startInfo.ArgumentList.Count > 0 && startInfo.ArgumentList[0] is "build" or "pack" or "msbuild" or "restore")
        {
            startInfo.ArgumentList.Add("-nodeReuse:false");
            startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        }

        var command = $"dotnet {string.Join(' ', startInfo.ArgumentList)}";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start 'dotnet'.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var deadline = Stopwatch.StartNew();

        if (!process.WaitForExit(timeout))
        {
            throw Timeout(process, output, error, $"'{command}' did not finish within {timeout}.");
        }

        var remaining = timeout - deadline.Elapsed;
        if (!Task.WaitAll([output, error], remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero))
        {
            throw Timeout(process, output, error, $"'{command}' exited, but its output streams stayed open past {timeout}.");
        }

        return (process.ExitCode, output.Result, error.Result);
    }

    private static TimeoutException Timeout(Process process, Task<string> output, Task<string> error, string message)
    {
        process.Kill(entireProcessTree: true);
        var drained = Task.WaitAll([output, error], DrainWait);
        var text = new StringBuilder(message);
        text.AppendLine().AppendLine(drained ? "--- stdout ---" : "--- stdout so far, streams still open ---");
        text.AppendLine(output.IsCompletedSuccessfully ? output.Result : "(not read)");
        text.AppendLine("--- stderr ---");
        text.Append(error.IsCompletedSuccessfully ? error.Result : "(not read)");
        return new TimeoutException(text.ToString());
    }
}
