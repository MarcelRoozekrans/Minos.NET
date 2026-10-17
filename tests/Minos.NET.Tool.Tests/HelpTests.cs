using ZeroAlloc.TestHelpers;
using static Minos.Tool.Tests.Harness;

namespace Minos.Tool.Tests;

public sealed class HelpTests
{
    private static async Task<string> HelpOf(params string[] args)
    {
        var run = await Invoke(null, args);
        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Stderr);
        return run.Stdout.ReplaceLineEndings("\n");
    }

    [Fact]
    public async Task Help_Calibrate_MatchesSnapshot() => TextSnapshot.VerifyText(await HelpOf("calibrate", "--help"));

    [Fact]
    public async Task Help_Report_MatchesSnapshot() => TextSnapshot.VerifyText(await HelpOf("report", "--help"));

    [Fact]
    public async Task Help_Conformance_MatchesSnapshot() => TextSnapshot.VerifyText(await HelpOf("conformance", "--help"));

    [Fact]
    public async Task Help_Tool_MatchesSnapshot() => TextSnapshot.VerifyText(await HelpOf("--help"));

    [Fact]
    public async Task Version_IsPrintedOnItsOwn()
    {
        var run = await Invoke(null, "--version");

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Stderr);
        Assert.Matches(@"^\d+\.\d+\.\d+", run.Stdout);
    }

    [Fact]
    public async Task Help_ForTheTool_ListsTheThreeCommands()
    {
        var help = await HelpOf("--help");

        Assert.Contains("calibrate", help, StringComparison.Ordinal);
        Assert.Contains("report", help, StringComparison.Ordinal);
        Assert.Contains("conformance", help, StringComparison.Ordinal);
    }
}
