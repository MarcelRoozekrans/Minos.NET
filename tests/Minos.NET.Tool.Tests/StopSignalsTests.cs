using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Minos.Tool.Tests;

public sealed class StopSignalsTests
{
    private const string SavingLine = "Stopping; press Ctrl+C again to quit at once.";

    [Fact]
    public void CtrlC_CtrlBreakOrCtrlBackslash_AndTermination_AreTheSignalsThatStopTheTool()
        => Assert.Equal([PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM], StopSignals.Signals);

    [Theory]
    [InlineData(PosixSignal.SIGINT)]
    [InlineData(PosixSignal.SIGQUIT)]
    [InlineData(PosixSignal.SIGTERM)]
    public void TheFirstSignal_CancelsTheToken_KeepsTheProcessAliveToSave_AndSaysSo(PosixSignal signal)
    {
        using var stderr = new StringWriter();
        using var signals = new StopSignals(stderr);
        var context = new PosixSignalContext(signal);

        signals.Handle(context);

        Assert.True(signals.Token.IsCancellationRequested);
        Assert.True(context.Cancel);
        Assert.Equal(SavingLine + Environment.NewLine, stderr.ToString());
    }

    [Theory]
    [InlineData(PosixSignal.SIGINT, PosixSignal.SIGINT)]
    [InlineData(PosixSignal.SIGTERM, PosixSignal.SIGINT)]
    [InlineData(PosixSignal.SIGINT, PosixSignal.SIGTERM)]
    public void ASecondSignal_EndsTheProcessTheDefaultWay(PosixSignal first, PosixSignal second)
    {
        using var stderr = new StringWriter();
        using var signals = new StopSignals(stderr);
        signals.Handle(new PosixSignalContext(first));
        var context = new PosixSignalContext(second);

        signals.Handle(context);

        Assert.False(context.Cancel);
        Assert.Equal(SavingLine + Environment.NewLine, stderr.ToString());
    }

    [Fact]
    public void ASignal_AfterDispose_EndsTheProcessTheDefaultWay()
    {
        using var stderr = new StringWriter();
        var signals = new StopSignals(stderr);
        signals.Dispose();
        var context = new PosixSignalContext(PosixSignal.SIGTERM);

        signals.Handle(context);

        Assert.False(context.Cancel);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public async Task ARealSigterm_CancelsTheToken()
    {
        // A CI job that hits its timeout sends SIGTERM. Windows has no kill command to send one, so this is Unix only.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var stderr = new StringWriter();
        using var signals = new StopSignals(stderr);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = signals.Token.Register(() => cancelled.TrySetResult());

        using (var kill = Process.Start(new ProcessStartInfo("kill", ["-TERM", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)]) { UseShellExecute = false })!)
        {
            await kill.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, kill.ExitCode);
        }

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(signals.Token.IsCancellationRequested);
        Assert.Contains(SavingLine, stderr.ToString(), StringComparison.Ordinal);
    }
}
