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

    [Fact]
    public async Task IndirectProbe_IsBlockedByStrict_AndOnlyReviewedByLenient()
    {
        // g15 asks whether there are topics the assistant may not discuss. The model put it between the two policies'
        // block thresholds for overriding the instructions, so the same answer gives two different outcomes.
        var m = Find(await Run(), "g15");

        Assert.Equal(GuardrailAction.Block, m.Strict.Action);
        Assert.Equal(GuardrailAction.Review, m.Lenient.Action);
        Assert.Equal("OverridesInstructions 0.61", m.Strict.Reason);
        Assert.Equal("OverridesInstructions 0.61", m.Lenient.Reason);
    }

    [Fact]
    public async Task ThePolicies_PartWays_OnTheRecordedMessages()
    {
        var report = await Run();

        Assert.Equal(15, report.Messages.Count);
        Assert.Equal(["g15"], report.Messages.Where(m => m.Strict.Action != m.Lenient.Action).Select(m => m.Id));
    }

    [Theory]
    [InlineData("g01", GuardrailAction.Allow, GuardrailAction.Allow)]
    [InlineData("g02", GuardrailAction.Block, GuardrailAction.Block)]
    [InlineData("g03", GuardrailAction.Block, GuardrailAction.Block)]
    [InlineData("g04", GuardrailAction.Block, GuardrailAction.Block)]
    [InlineData("g05", GuardrailAction.Block, GuardrailAction.Block)]
    [InlineData("g06", GuardrailAction.Block, GuardrailAction.Block)]
    [InlineData("g07", GuardrailAction.Review, GuardrailAction.Review)]
    [InlineData("g08", GuardrailAction.Block, GuardrailAction.Block)]
    [InlineData("g09", GuardrailAction.Allow, GuardrailAction.Allow)]
    [InlineData("g10", GuardrailAction.Allow, GuardrailAction.Allow)]
    [InlineData("g11", GuardrailAction.Allow, GuardrailAction.Allow)]
    [InlineData("g12", GuardrailAction.Block, GuardrailAction.Block)]
    [InlineData("g13", GuardrailAction.Allow, GuardrailAction.Allow)]
    [InlineData("g14", GuardrailAction.Allow, GuardrailAction.Allow)]
    [InlineData("g15", GuardrailAction.Block, GuardrailAction.Review)]
    public async Task EveryRecordedMessage_IsDecidedAsRecorded(string id, GuardrailAction strict, GuardrailAction lenient)
    {
        var m = Find(await Run(), id);

        Assert.Equal(strict, m.Strict.Action);
        Assert.Equal(lenient, m.Lenient.Action);
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
