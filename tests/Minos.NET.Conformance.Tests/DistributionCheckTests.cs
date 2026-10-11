using static Minos.Conformance.Tests.Outcomes;

namespace Minos.Conformance.Tests;

public sealed class DistributionCheckTests
{
    private static ProbeOutcome ChoiceWith(string probabilities)
        => Answered(
            ConformanceProbe.Choice,
            """{"answers":{"department":{"type":"choice","choice":"billing","probabilities":{""" + probabilities + """},"confidence":0.81}}}""",
            null);

    private static ProbeOutcome ScoreWith(string score, string probabilities = "\"0\":0.0,\"1\":0.95,\"2\":0.05")
        => Answered(
            ConformanceProbe.Score,
            """{"answers":{"frustration":{"type":"score","score":""" + score + ""","probabilities":{""" + probabilities + """},"confidence":0.92}}}""",
            null);

    [Fact]
    public void D1_FiniteProbabilitiesInRange_Pass()
        => Assert.Equal(
            new CheckResult("D1", CheckOutcome.Pass, "All 6 probabilities are finite and in [0, 1]."),
            ConformanceChecks.D1(Answered(ConformanceProbe.Mixed, MixedBody, null), ConformanceWire.SystemOne));

    [Fact]
    public void D1_ANoulAboveOne_Fails()
    {
        var result = ConformanceChecks.D1(Answered(ConformanceProbe.Noul, """{"answers":{"is_urgent":{"type":"noul","noul":1.2}}}""", null), ConformanceWire.SystemOne);

        Assert.Equal(new CheckResult("D1", CheckOutcome.Fail, "'is_urgent' is 1.2."), result);
    }

    [Fact]
    public void D1_ANegativeOrNonNumericProbability_Fails()
        => Assert.Equal(
            "'department' gives 'billing' -0.1; 'department' gives 'technical' no finite number.",
            ConformanceChecks.D1(ChoiceWith("\"billing\":-0.1,\"technical\":\"high\""), ConformanceWire.SystemOne).Detail);

    [Fact]
    public void D1_ReadsDecisionsArrays()
    {
        const string Body = """{"answers":[{"type":"choice","name":"department","choice":"billing","probabilities":[{"value":"billing","probability":0.8},{"value":"technical","probability":1.5}],"confidence":0.6}]}""";

        Assert.Equal(
            "'department' gives 'technical' 1.5.",
            ConformanceChecks.D1(Answered(ConformanceProbe.Choice, Body, null), ConformanceWire.Decisions).Detail);
    }

    [Fact]
    public void D2_ASumOffByExactlyTheTolerance_Passes()
        => Assert.Equal(CheckOutcome.Pass, ConformanceChecks.D2(ChoiceWith("\"billing\":0.5,\"technical\":0.49"), ConformanceWire.SystemOne).Outcome);

    [Fact]
    public void D2_ASumOffByMoreThanTheTolerance_Fails()
        => Assert.Equal(
            new CheckResult("D2", CheckOutcome.Fail, "'department' sums to 0.985."),
            ConformanceChecks.D2(ChoiceWith("\"billing\":0.5,\"technical\":0.485"), ConformanceWire.SystemOne));

    [Fact]
    public void D2_AMissingOption_Fails()
        => Assert.Equal(
            "'department' has no probability for 'technical'.",
            ConformanceChecks.D2(ChoiceWith("\"billing\":1.0"), ConformanceWire.SystemOne).Detail);

    [Fact]
    public void D2_AnOptionThatWasNotSent_Fails()
        => Assert.Equal(
            "'department' has a probability for 'sales', which was not sent.",
            ConformanceChecks.D2(ChoiceWith("\"billing\":0.8,\"technical\":0.2,\"sales\":0.0"), ConformanceWire.SystemOne).Detail);

    [Fact]
    public void D2_ReadsDecisionsScoreArrays()
    {
        const string Body = """{"answers":[{"type":"score","name":"frustration","score":1.1,"probabilities":[{"value":0,"label":"0","probability":0.1},{"value":1,"label":"1","probability":0.7},{"value":2,"label":"2","probability":0.2}],"confidence":0.5}]}""";

        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.D2(Answered(ConformanceProbe.Score, Body, null), ConformanceWire.Decisions).Outcome);
    }

    [Fact]
    public void D2_ANoulOnlyProbe_IsSkipped()
        => Assert.Equal(
            new CheckResult("D2", CheckOutcome.Skipped, "The probe asks no Choice or Score question."),
            ConformanceChecks.D2(Answered(ConformanceProbe.Noul, NoulBody, null), ConformanceWire.SystemOne));

    [Fact]
    public void D3_AScoreOffByExactlyTheTolerance_Passes()
        => Assert.Equal(CheckOutcome.Pass, ConformanceChecks.D3(ScoreWith("1.06"), ConformanceWire.SystemOne).Outcome);

    [Fact]
    public void D3_AScoreOffByMoreThanTheTolerance_Fails()
        => Assert.Equal(
            new CheckResult("D3", CheckOutcome.Fail, "'frustration' scores 1.07, but its probabilities give 1.05."),
            ConformanceChecks.D3(ScoreWith("1.07"), ConformanceWire.SystemOne));

    [Fact]
    public void D3_ALevelThatIsNotANumber_Fails()
        => Assert.Equal(
            "'frustration' has a level 'calm' that is not a number.",
            ConformanceChecks.D3(ScoreWith("1.0", "\"calm\":0.0,\"1\":1.0"), ConformanceWire.SystemOne).Detail);

    [Fact]
    public void D3_AChoiceProbe_IsSkipped()
        => Assert.Equal(CheckOutcome.Skipped, ConformanceChecks.D3(Answered(ConformanceProbe.Choice, ChoiceBody, null), ConformanceWire.SystemOne).Outcome);

    [Fact]
    public void D4_ConfidencesInRange_Pass()
        => Assert.Equal(
            new CheckResult("D4", CheckOutcome.Pass, "All 2 confidences are in [0, 1]."),
            ConformanceChecks.D4(Answered(ConformanceProbe.Mixed, MixedBody, null), ConformanceWire.SystemOne));

    [Fact]
    public void D4_AConfidenceAboveOne_Fails()
    {
        const string Body = """{"answers":{"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":1.5}}}""";

        Assert.Equal("'department' has confidence 1.5.", ConformanceChecks.D4(Answered(ConformanceProbe.Choice, Body, null), ConformanceWire.SystemOne).Detail);
    }

    [Fact]
    public void D4_NoConfidenceReported_IsSkipped()
        => Assert.Equal(
            new CheckResult("D4", CheckOutcome.Skipped, "No answer reports a confidence."),
            ConformanceChecks.D4(Answered(ConformanceProbe.Noul, NoulBody, null), ConformanceWire.SystemOne));

    [Fact]
    public void D1_NoAnswers_IsSkipped()
        => Assert.Equal(
            new CheckResult("D1", CheckOutcome.Skipped, "No answer to check; S1 reports it."),
            ConformanceChecks.D1(Answered(ConformanceProbe.Noul, """{"answers":{}}""", null), ConformanceWire.SystemOne));

    [Fact]
    public void D2_NoAnswers_IsSkipped()
        => Assert.Equal(
            new CheckResult("D2", CheckOutcome.Skipped, "No answer to check; S1 reports it."),
            ConformanceChecks.D2(Answered(ConformanceProbe.Choice, """{"answers":{}}""", null), ConformanceWire.SystemOne));

    [Fact]
    public void D3_NoAnswers_IsSkipped()
        => Assert.Equal(
            new CheckResult("D3", CheckOutcome.Skipped, "No answer to check; S1 reports it."),
            ConformanceChecks.D3(Answered(ConformanceProbe.Score, """{"answers":{}}""", null), ConformanceWire.SystemOne));

    [Fact]
    public void D2_AndD3_ReadADecisionsScoreLevelByValue_NotByLabel()
    {
        const string Body = """{"answers":[{"type":"score","name":"frustration","score":1.05,"probabilities":[{"value":0,"label":"Calm","probability":0.0},{"value":1,"label":"Frustrated","probability":0.95},{"value":2,"label":"Furious","probability":0.05}],"confidence":0.5}]}""";
        var outcome = Answered(ConformanceProbe.Score, Body, null);

        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.D2(outcome, ConformanceWire.Decisions).Outcome);
        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.D3(outcome, ConformanceWire.Decisions).Outcome);
    }
}
