using System.Diagnostics;

namespace Minos.PackTests;

/// <summary>
/// Runs <c>dotnet</c> with its output captured, safely. A build started by the child must not leave MSBuild worker nodes
/// or the compiler server holding the redirected pipes, or reading them never ends (#145). So node reuse and the shared
/// compiler are off for the child, both streams are read at the same time so neither pipe can fill and block the other,
/// and the wait has a time limit that fails with the output so far.
/// </summary>
internal static class DotnetProcess
{
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start 'dotnet'.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'dotnet {string.Join(' ', startInfo.ArgumentList)}' did not finish within {timeout}.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
