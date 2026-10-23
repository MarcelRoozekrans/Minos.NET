using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Minos.Tool.Tests;

public sealed class StopSignalsTests
{
    [Fact]
    public void CtrlC_CtrlBreak_AndTermination_AreTheSignalsThatStopTheTool()
        => Assert.Equal([PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM], StopSignals.Signals);

    [Theory]
    [InlineData(PosixSignal.SIGINT)]
    [InlineData(PosixSignal.SIGQUIT)]
    [InlineData(PosixSignal.SIGTERM)]
    public void ASignal_CancelsTheToken_AndKeepsTheProcessAliveToSave(PosixSignal signal)
    {
        using var signals = new StopSignals();
        var context = new PosixSignalContext(signal);

        signals.Handle(context);

        Assert.True(signals.Token.IsCancellationRequested);
        Assert.True(context.Cancel);
    }

    [Fact]
    public void ASignal_AfterDispose_IsIgnored()
    {
        var signals = new StopSignals();
        signals.Dispose();
        var context = new PosixSignalContext(PosixSignal.SIGTERM);

        signals.Handle(context);

        Assert.True(context.Cancel);
    }

    [Fact]
    public async Task ARealSigterm_CancelsTheToken()
    {
        // A CI job that hits its timeout sends SIGTERM. Windows has no kill command to send one, so this is Unix only.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var signals = new StopSignals();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = signals.Token.Register(() => cancelled.TrySetResult());

        using (var kill = Process.Start(new ProcessStartInfo("kill", ["-TERM", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)]) { UseShellExecute = false })!)
        {
            await kill.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, kill.ExitCode);
        }

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(signals.Token.IsCancellationRequested);
    }
}
