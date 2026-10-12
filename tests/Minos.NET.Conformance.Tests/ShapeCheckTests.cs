using static Minos.Conformance.Tests.Outcomes;

namespace Minos.Conformance.Tests;

public sealed class ShapeCheckTests
{
    [Fact]
    public void S1_OneAnswerPerQuestion_Passes()
    {
        var result = ConformanceChecks.S1(Answered(ConformanceProbe.Noul, NoulBody, Noul(0.95)), ConformanceWire.SystemOne);

        Assert.Equal(new CheckResult("S1", CheckOutcome.Pass, "One answer for each question asked, 1 in all."), result);
    }

    [Fact]
    public void S1_AnExtraAnswer_Fails()
    {
        const string Body = """{"answers":{"is_urgent":{"type":"noul","noul":0.95},"is_angry":{"type":"noul","noul":0.1}}}""";

        var result = ConformanceChecks.S1(Answered(ConformanceProbe.Noul, Body, Noul(0.95)), ConformanceWire.SystemOne);

        Assert.Equal(CheckOutcome.Fail, result.Outcome);
        Assert.Equal("An answer for 'is_angry', which was not asked.", result.Detail);
    }

    [Fact]
    public void S1_AMissingAnswer_Fails()
    {
        var result = ConformanceChecks.S1(Answered(ConformanceProbe.Noul, """{"answers":{}}""", null), ConformanceWire.SystemOne);

        Assert.Equal("No answer for 'is_urgent'.", result.Detail);
    }

    [Fact]
    public void S1_TwoAnswersForOneName_FailOnDecisions()
    {
        const string Body = """{"answers":[{"type":"predicate","name":"is_urgent","probability":0.9},{"type":"predicate","name":"is_urgent","probability":0.8}]}""";

        var result = ConformanceChecks.S1(Answered(ConformanceProbe.Noul, Body, null), ConformanceWire.Decisions);

        Assert.Equal("Two answers for 'is_urgent'.", result.Detail);
    }

    [Fact]
    public void S1_AnArrayOnSystemOne_Fails()
    {
        const string Body = """{"answers":[{"type":"noul","name":"is_urgent","noul":0.9}]}""";

        var result = ConformanceChecks.S1(Answered(ConformanceProbe.Noul, Body, null), ConformanceWire.SystemOne);

        Assert.Equal(new CheckResult("S1", CheckOutcome.Fail, "The answers are not a JSON object keyed by question."), result);
    }

    [Fact]
    public void S1_ABodyThatIsNotAnObject_Fails()
        => Assert.Equal(
            "The response body is not a JSON object.",
            ConformanceChecks.S1(Answered(ConformanceProbe.Noul, "[]", null), ConformanceWire.SystemOne).Detail);

    [Fact]
    public void S1_AnErrorStatus_Fails()
        => Assert.Equal(
            "HTTP 500: the response is an error, not answers.",
            ConformanceChecks.S1(Failed(ConformanceProbe.Noul, 500, "{}", null), ConformanceWire.SystemOne).Detail);

    [Fact]
    public void S1_NothingSent_IsSkipped()
    {
        var outcome = new ProbeOutcome(ConformanceProbe.Noul, [], null, new DecisionError(DecisionErrorKind.Unsupported, "No Noul here."));

        var result = ConformanceChecks.S1(outcome, ConformanceWire.SystemOne);

        Assert.Equal(new CheckResult("S1", CheckOutcome.Skipped, "No request was sent: Unsupported, No Noul here."), result);
    }

    [Fact]
    public void S2_NoulIsNoulOnSystemOne_AndPredicateOnDecisions()
    {
        const string Decisions = """{"answers":[{"type":"predicate","name":"is_urgent","probability":0.95}]}""";

        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.S2(Answered(ConformanceProbe.Noul, NoulBody, null), ConformanceWire.SystemOne).Outcome);
        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.S2(Answered(ConformanceProbe.Noul, Decisions, null), ConformanceWire.Decisions).Outcome);
    }

    [Fact]
    public void S2_PredicateOnSystemOne_Fails()
    {
        const string Body = """{"answers":{"is_urgent":{"type":"predicate","noul":0.95}}}""";

        var result = ConformanceChecks.S2(Answered(ConformanceProbe.Noul, Body, null), ConformanceWire.SystemOne);

        Assert.Equal(new CheckResult("S2", CheckOutcome.Fail, "'is_urgent' is 'predicate', not 'noul'."), result);
    }

    [Fact]
    public void S2_AWrongOrMissingType_Fails()
    {
        const string Body = """{"answers":{"department":{"type":"score","choice":"billing"},"is_urgent":{"noul":0.9},"frustration":{"type":"score","score":1}}}""";

        var result = ConformanceChecks.S2(Answered(ConformanceProbe.Mixed, Body, null), ConformanceWire.SystemOne);

        Assert.Equal("'is_urgent' is untyped, not 'noul'; 'department' is 'score', not 'choice'.", result.Detail);
    }

    [Fact]
    public void S3_MinosReadsEveryKey_Passes()
        => Assert.Equal(
            new CheckResult("S3", CheckOutcome.Pass, "Minos read every answer by key, 1 in all."),
            ConformanceChecks.S3(Answered(ConformanceProbe.Noul, NoulBody, Noul(0.95))));

    [Fact]
    public void S3_AnError_Fails()
    {
        var outcome = Failed(ConformanceProbe.Noul, 200, NoulBody, new DecisionError(DecisionErrorKind.InvalidResponse, "The answer for 'is_urgent' is not a number."));

        Assert.Equal(
            new CheckResult("S3", CheckOutcome.Fail, "Minos returned InvalidResponse: The answer for 'is_urgent' is not a number."),
            ConformanceChecks.S3(outcome));
    }

    [Fact]
    public void S3_AnswersForOtherKeys_Fail()
    {
        var other = new DecisionResponse(new QuestionSetDefinition(QuestionDefinition.Noul("is_calm", "Calm?")), [QuestionAnswer.Noul(0.5)]);

        var result = ConformanceChecks.S3(Answered(ConformanceProbe.Noul, NoulBody, other));

        Assert.Equal("Answer 0 is 'is_calm', not 'is_urgent'.", result.Detail);
    }

    [Fact]
    public void S2_NoAnswers_IsSkipped()
        => Assert.Equal(
            new CheckResult("S2", CheckOutcome.Skipped, "No answer to check; S1 reports it."),
            ConformanceChecks.S2(Answered(ConformanceProbe.Noul, """{"answers":{}}""", null), ConformanceWire.SystemOne));
}
