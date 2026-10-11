using static Minos.Conformance.Tests.Outcomes;

namespace Minos.Conformance.Tests;

public sealed class ModelCheckTests
{
    private const string GuideBody = """{"answers":[{"type":"predicate","name":"is_urgent","probability":0.92}]}""";

    [Fact]
    public void R1_TheReportedModelIsDecisionResponseModel_Passes()
        => Assert.Equal(
            new CheckResult("R1", CheckOutcome.Pass, "The response names jev-1.13.0, and DecisionResponse.Model is the same."),
            ConformanceChecks.R1(Answered(ConformanceProbe.Noul, NoulBody, Noul(0.95)), SystemOneTarget));

    [Fact]
    public void R1_ADifferentDecisionResponseModel_Fails()
        => Assert.Equal(
            "The response names jev-1.13.0, but DecisionResponse.Model is null.",
            ConformanceChecks.R1(Answered(ConformanceProbe.Noul, NoulBody, Noul(0.95, model: null)), SystemOneTarget).Detail);

    [Fact]
    public void R1_NoModel_FailsForATargetWithARule_AndIsSkippedForOneWithout()
    {
        var outcome = Answered(ConformanceProbe.Noul, GuideBody, Noul(0.92, model: null));

        Assert.Equal(new CheckResult("R1", CheckOutcome.Fail, "The response names no model."), ConformanceChecks.R1(outcome, SystemOneTarget));
        Assert.Equal(
            new CheckResult("R1", CheckOutcome.Skipped, "The response names no model, and the target promises none."),
            ConformanceChecks.R1(outcome, DecisionsTarget));
    }

    [Fact]
    public void R2_AVersionedIdForTheAlias_Passes()
        => Assert.Equal(
            new CheckResult("R2", CheckOutcome.Pass, "jev-1.13.0 answers jev-latest."),
            ConformanceChecks.R2(Answered(ConformanceProbe.Noul, NoulBody, Noul(0.95)), SystemOneTarget));

    [Fact]
    public void R2_AnotherModel_Fails()
    {
        const string Body = """{"model":"gpt-6-luna","answers":{"is_urgent":{"type":"noul","noul":0.95}}}""";

        var result = ConformanceChecks.R2(Answered(ConformanceProbe.Noul, Body, Noul(0.95, "gpt-6-luna")), SystemOneTarget);

        Assert.Equal(CheckOutcome.Fail, result.Outcome);
        Assert.Equal("gpt-6-luna does not fit jev-latest. " + ModelRule.TypeSafe.Description, result.Detail);
    }

    [Fact]
    public void R2_NoRule_IsSkipped()
        => Assert.Equal(
            new CheckResult("R2", CheckOutcome.Skipped, "The target states no model rule."),
            ConformanceChecks.R2(Answered(ConformanceProbe.Noul, GuideBody, Noul(0.92, model: null)), DecisionsTarget));

    [Fact]
    public void R3_NonNegativeTokenCounts_Pass()
        => Assert.Equal(
            new CheckResult("R3", CheckOutcome.Pass, "input_tokens 296, output_tokens 20."),
            ConformanceChecks.R3(Answered(ConformanceProbe.Noul, NoulBody, Noul(0.95))));

    [Fact]
    public void R3_ANegativeOrFractionalCount_Fails()
    {
        const string Body = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":-1,"output_tokens":2.5}}""";

        Assert.Equal(
            "Usage: input_tokens is -1; output_tokens is 2.5.",
            ConformanceChecks.R3(Answered(ConformanceProbe.Noul, Body, Noul(0.95))).Detail);
    }

    [Fact]
    public void R3_NoUsage_IsSkipped()
        => Assert.Equal(
            new CheckResult("R3", CheckOutcome.Skipped, "The response reports no usage."),
            ConformanceChecks.R3(Answered(ConformanceProbe.Noul, GuideBody, Noul(0.92, model: null))));
}
