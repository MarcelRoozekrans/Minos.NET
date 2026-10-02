using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Jev.Samples.Guardrails;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Jev.Samples.Tests;

public sealed class GuardrailsSampleTests
{
    private const string Sample = "ZeroAlloc.Jev.Samples.Guardrails";

    [Fact]
    public async Task Jailbreaks_AreBlockedUnderBothPolicies()
    {
        var report = await Run();

        foreach (var id in new[] { "g02", "g03", "g12" })
        {
            var m = Find(report, id);
            Assert.Equal(GuardrailAction.Block, m.Strict.Action);
            Assert.Equal(GuardrailAction.Block, m.Lenient.Action);
        }
    }

    [Fact]
    public async Task OrdinaryMessages_AreAllowedUnderBothPolicies()
    {
        var report = await Run();

        foreach (var id in new[] { "g01", "g09", "g10" })
        {
            var m = Find(report, id);
            Assert.Equal(GuardrailAction.Allow, m.Strict.Action);
            Assert.Equal(GuardrailAction.Allow, m.Lenient.Action);
        }
    }

    [Fact]
    public async Task Strict_IsNeverMoreLenientThanLenient()
    {
        var report = await Run();

        Assert.All(report.Messages, m => Assert.True(m.Strict.Action >= m.Lenient.Action, m.Id));
    }

    private static ScreenedMessage Find(GuardrailsReport report, string id) =>
        report.Messages.First(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    [Fact]
    public async Task Report_MatchesTheSnapshot() => TextSnapshot.VerifyText((await Run()).Render());

    private static async Task<GuardrailsReport> Run()
    {
        using var provider = SampleHost.BuildReplayProvider(SampleHost.SampleDirectory(Sample), Sample);
        return await GuardrailsSample.RunAsync(provider.GetRequiredService<IJevClient>(), CancellationToken.None);
    }
}
