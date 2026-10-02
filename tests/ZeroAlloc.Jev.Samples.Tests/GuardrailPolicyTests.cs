using ZeroAlloc.Jev.Samples.Guardrails;

namespace ZeroAlloc.Jev.Samples.Tests;

public sealed class GuardrailPolicyTests
{
    [Fact]
    public async Task ReviewThreshold_IsInclusive()
    {
        var screen = await CannedScreen.Of(abusive: 0.3);

        Assert.Equal(GuardrailAction.Review, GuardrailPolicy.Strict.Decide(screen).Action);
    }

    [Fact]
    public async Task JustBelowReviewThreshold_Allows()
    {
        var screen = await CannedScreen.Of(abusive: 0.29);

        Assert.Equal(GuardrailAction.Allow, GuardrailPolicy.Strict.Decide(screen).Action);
    }

    [Fact]
    public async Task BlockThreshold_IsInclusive()
    {
        var screen = await CannedScreen.Of(abusive: 0.6);

        Assert.Equal(GuardrailAction.Block, GuardrailPolicy.Strict.Decide(screen).Action);
    }

    [Fact]
    public async Task JustBelowBlockThreshold_Reviews()
    {
        var screen = await CannedScreen.Of(abusive: 0.59);

        Assert.Equal(GuardrailAction.Review, GuardrailPolicy.Strict.Decide(screen).Action);
    }

    [Fact]
    public async Task HighestActionWins_WhenTwoHazardsTrigger()
    {
        var screen = await CannedScreen.Of(abusive: 0.35, personalData: 0.8);

        var decision = GuardrailPolicy.Strict.Decide(screen);

        Assert.Equal(GuardrailAction.Block, decision.Action);
        Assert.Contains("IsAbusive", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SharesPersonalData", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_BecomesBlock_AtTheSeverityThreshold()
    {
        var screen = await CannedScreen.Of(abusive: 0.4, severity: 1.5);

        var decision = GuardrailPolicy.Strict.Decide(screen);

        Assert.Equal(GuardrailAction.Block, decision.Action);
        Assert.Contains("severity", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_StaysReview_JustBelowTheSeverityThreshold()
    {
        var screen = await CannedScreen.Of(abusive: 0.4, severity: 1.49);

        Assert.Equal(GuardrailAction.Review, GuardrailPolicy.Strict.Decide(screen).Action);
    }

    [Fact]
    public async Task Severity_AloneNeverBlocks_WhenNoHazardTriggers()
    {
        var screen = await CannedScreen.Of(severity: 3);

        Assert.Equal(GuardrailAction.Allow, GuardrailPolicy.Strict.Decide(screen).Action);
    }

    [Fact]
    public async Task LenientSeverityThreshold_IsHigherThanStricts()
    {
        var screen = await CannedScreen.Of(abusive: 0.6, severity: 2);

        Assert.Equal(GuardrailAction.Block, GuardrailPolicy.Strict.Decide(screen).Action);
        Assert.Equal(GuardrailAction.Review, GuardrailPolicy.Lenient.Decide(screen).Action);
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(1.0)]
    public async Task AdviceRequests_NeverBlockOnTheirOwnProbability(double advice)
    {
        var screen = await CannedScreen.Of(advice: advice);

        Assert.Equal(GuardrailAction.Review, GuardrailPolicy.Strict.Decide(screen).Action);
        Assert.Equal(GuardrailAction.Review, GuardrailPolicy.Lenient.Decide(screen).Action);
    }

    [Fact]
    public async Task HarmfulAdviceRequest_IsBlockedBySeverity()
    {
        var screen = await CannedScreen.Of(advice: 0.95, severity: 2.8);

        Assert.Equal(GuardrailAction.Block, GuardrailPolicy.Strict.Decide(screen).Action);
        Assert.Equal(GuardrailAction.Block, GuardrailPolicy.Lenient.Decide(screen).Action);
    }

    [Fact]
    public async Task Strict_ReviewsWhatLenientAllows()
    {
        var screen = await CannedScreen.Of(abusive: 0.4);

        Assert.Equal(GuardrailAction.Review, GuardrailPolicy.Strict.Decide(screen).Action);
        Assert.Equal(GuardrailAction.Allow, GuardrailPolicy.Lenient.Decide(screen).Action);
    }
}
