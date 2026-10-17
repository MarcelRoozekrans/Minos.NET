namespace Minos.Tool.Tests;

public sealed class GuardTests
{
    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(FileLoadException))]
    public async Task AMissingFileOrAssembly_IsNotReportedAsAnOutputProblem(Type exception)
    {
        using var stderr = new StringWriter();

        var exit = await CommandSupport.GuardAsync(stderr, "The run failed", () => throw (Exception)Activator.CreateInstance(exception, "Minos.NET.Evaluation.dll")!, CancellationToken.None);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("missing or cannot be loaded", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("output cannot be written", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIOFailure_IsStillReportedAsAnOutputProblem()
    {
        using var stderr = new StringWriter();

        var exit = await CommandSupport.GuardAsync(stderr, "The run failed", () => throw new IOException("disk full"), CancellationToken.None);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("The output cannot be written: disk full", stderr.ToString(), StringComparison.Ordinal);
    }
}
