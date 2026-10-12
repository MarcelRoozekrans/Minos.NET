using static Minos.Conformance.Tests.Outcomes;

namespace Minos.Conformance.Tests;

public sealed class MultiQuestionCheckTests
{
    [Fact]
    public void M1_AnswersMatchedByKey_Pass()
        => Assert.Equal(
            new CheckResult("M1", CheckOutcome.Pass, "Minos matched the 3 answers by key; the response listed them as department, frustration, is_urgent."),
            ConformanceChecks.M1(Answered(ConformanceProbe.Mixed, MixedBody, Mixed(0.95, 0, 1.05)), ConformanceWire.SystemOne));

    [Fact]
    public void M1_AReadingByPosition_Fails()
    {
        // A parser that took the first answer, department's, for is_urgent would have read another value.
        var result = ConformanceChecks.M1(Answered(ConformanceProbe.Mixed, MixedBody, Mixed(0.88, 0, 1.05)), ConformanceWire.SystemOne);

        Assert.Equal(new CheckResult("M1", CheckOutcome.Fail, "Minos read 'is_urgent' as 0.88, but the response says 0.95."), result);
    }

    [Fact]
    public void M1_AnotherChosenOption_Fails()
        => Assert.Equal(
            "Minos read 'department' as 'technical', but the response chose 'billing'.",
            ConformanceChecks.M1(Answered(ConformanceProbe.Mixed, MixedBody, Mixed(0.95, 1, 1.05)), ConformanceWire.SystemOne).Detail);

    [Fact]
    public void M1_ReadsDecisionsNames()
    {
        const string Body = """{"answers":[{"type":"choice","name":"department","choice":"billing","probabilities":[{"value":"billing","probability":0.88},{"value":"technical","probability":0.12}],"confidence":0.81},{"type":"score","name":"frustration","score":1.05,"probabilities":[{"value":0,"label":"0","probability":0.0},{"value":1,"label":"1","probability":0.95},{"value":2,"label":"2","probability":0.05}],"confidence":0.92},{"type":"predicate","name":"is_urgent","probability":0.95}]}""";

        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.M1(Answered(ConformanceProbe.Mixed, Body, Mixed(0.95, 0, 1.05)), ConformanceWire.Decisions).Outcome);
    }

    [Fact]
    public void M1_AnError_IsSkipped()
        => Assert.Equal(
            new CheckResult("M1", CheckOutcome.Skipped, "Minos returned an error; S3 reports it."),
            ConformanceChecks.M1(Failed(ConformanceProbe.Mixed, 200, MixedBody, new DecisionError(DecisionErrorKind.InvalidResponse, "x")), ConformanceWire.SystemOne));

    [Fact]
    public void M1_AppliesToTheMixedProbeOnly()
    {
        Assert.Contains("M1", ConformanceChecks.For(ConformanceProbe.Mixed));
        Assert.All(ConformanceProbe.All.Where(probe => probe != ConformanceProbe.Mixed), probe => Assert.DoesNotContain("M1", ConformanceChecks.For(probe)));
    }
}
