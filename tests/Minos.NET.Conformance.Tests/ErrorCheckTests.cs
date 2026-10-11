using static Minos.Conformance.Tests.Outcomes;

namespace Minos.Conformance.Tests;

public sealed class ErrorCheckTests
{
    private const string ValidationBody = """{"detail":[{"type":"too_short","loc":["body","questions"],"msg":"Dictionary should have at least 1 item after validation, not 0"}]}""";

    private const string UnauthorizedBody = """{"detail":{"error_type":"authentication_error","message":"Cannot authenticate with the server."}}""";

    [Fact]
    public void E1_A422WithAJsonBodyMappedToValidation_Passes()
    {
        var outcome = Failed(ConformanceProbe.Invalid, 422, ValidationBody, Error(DecisionErrorKind.Validation, 422, ValidationBody));

        Assert.Equal(
            new CheckResult("E1", CheckOutcome.Pass, "HTTP 422 with a JSON body, mapped to Validation: The API returned HTTP 422."),
            ConformanceChecks.E1(outcome));
    }

    [Fact]
    public void E1_ALenientProviderThatAnswers_Fails()
        => Assert.Equal(
            new CheckResult("E1", CheckOutcome.Fail, "HTTP 200, not 400 or 422: the provider did not reject the request."),
            ConformanceChecks.E1(Answered(ConformanceProbe.Invalid, """{"answers":{}}""", null)));

    [Fact]
    public void E1_ABodyThatIsNotJson_Fails()
    {
        var exchange = ConformanceExchange.FromHttp(ConformanceProbe.Invalid.Id, requestBody: null, 400, "text/plain", headers: null, "Bad request");
        var outcome = new ProbeOutcome(
            ConformanceProbe.Invalid,
            [exchange],
            null,
            new DecisionError(DecisionErrorKind.Validation, "The API returned HTTP 400.") { StatusCode = 400 });

        Assert.Equal("The error body is not JSON.", ConformanceChecks.E1(outcome).Detail);
    }

    [Fact]
    public void E1_AJsonBodyWithoutDetail_BlamesMinos()
    {
        var outcome = Failed(ConformanceProbe.Invalid, 400, "{}", new DecisionError(DecisionErrorKind.Validation, "The API returned HTTP 400.") { StatusCode = 400 });

        Assert.Equal("The error body is JSON, but Minos left DecisionError.Detail null.", ConformanceChecks.E1(outcome).Detail);
    }

    [Fact]
    public void E1_AnotherKind_Fails()
        => Assert.Equal(
            "Minos mapped it to Http, not Validation.",
            ConformanceChecks.E1(Failed(ConformanceProbe.Invalid, 422, ValidationBody, Error(DecisionErrorKind.Http, 422, ValidationBody))).Detail);

    [Fact]
    public void E2_A401MappedToUnauthorizedAndSentOnce_Passes()
        => Assert.Equal(
            new CheckResult("E2", CheckOutcome.Pass, "HTTP 401, mapped to Unauthorized, one request sent under the client's retry setting."),
            ConformanceChecks.E2(Failed(ConformanceProbe.BadKey, 401, UnauthorizedBody, Error(DecisionErrorKind.Unauthorized, 401, UnauthorizedBody))));

    [Fact]
    public void E2_AnAcceptedKey_Fails()
        => Assert.Equal(
            "HTTP 200, not 401 or 403.",
            ConformanceChecks.E2(Answered(ConformanceProbe.BadKey, Outcomes.NoulBody, Noul(0.95))).Detail);

    [Fact]
    public void E2_ARetried401_Fails()
    {
        var exchange = Exchange(ConformanceProbe.BadKey, 401, UnauthorizedBody);
        var outcome = new ProbeOutcome(ConformanceProbe.BadKey, [exchange, exchange], null, Error(DecisionErrorKind.Unauthorized, 401, UnauthorizedBody));

        Assert.Equal("The request was sent 2 times; an Unauthorized error is not retried.", ConformanceChecks.E2(outcome).Detail);
    }

    [Fact]
    public void E3_TheHeaderEqualsTheRequestId_Passes()
    {
        var outcome = Failed(
            ConformanceProbe.BadKey, 401, UnauthorizedBody, Error(DecisionErrorKind.Unauthorized, 401, UnauthorizedBody, requestId: "req_1"), ("x-typesafe-request-id", "req_1"));

        Assert.Equal(new CheckResult("E3", CheckOutcome.Pass, "x-typesafe-request-id req_1 is DecisionError.RequestId."), ConformanceChecks.E3(outcome));
    }

    [Fact]
    public void E3_ADifferentRequestId_Fails()
    {
        var outcome = Failed(
            ConformanceProbe.BadKey, 401, UnauthorizedBody, Error(DecisionErrorKind.Unauthorized, 401, UnauthorizedBody), ("x-request-id", "req_2"));

        Assert.Equal(new CheckResult("E3", CheckOutcome.Fail, "x-request-id is req_2, but DecisionError.RequestId is null."), ConformanceChecks.E3(outcome));
    }

    [Fact]
    public void E3_ReadsTheHeadersInTheOrderMinosDoes()
    {
        var outcome = Failed(
            ConformanceProbe.Invalid,
            422,
            ValidationBody,
            Error(DecisionErrorKind.Validation, 422, ValidationBody, requestId: "req_first"),
            ("request-id", "req_third"),
            ("x-request-id", "req_first"));

        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.E3(outcome).Outcome);
        Assert.Equal(["x-request-id", "x-typesafe-request-id", "request-id"], ConformanceChecks.RequestIdHeaders);
    }

    [Fact]
    public void E3_NoRequestIdHeader_IsSkipped()
        => Assert.Equal(
            new CheckResult("E3", CheckOutcome.Skipped, "The error response carries no request-id header: x-request-id, x-typesafe-request-id or request-id."),
            ConformanceChecks.E3(Failed(ConformanceProbe.BadKey, 401, UnauthorizedBody, Error(DecisionErrorKind.Unauthorized, 401, UnauthorizedBody))));

    [Fact]
    public void E4_No429_IsSkipped()
        => Assert.Equal(
            new CheckResult("E4", CheckOutcome.Skipped, "No 429 was met during the run."),
            ConformanceChecks.E4([Answered(ConformanceProbe.Noul, NoulBody, Noul(0.95))]));

    [Fact]
    public void E4_A429WithItsRetryAfterAndRequestIdRead_Passes()
    {
        var outcome = Failed(
            ConformanceProbe.Noul,
            429,
            "{}",
            Error(DecisionErrorKind.RateLimited, 429, "{}", requestId: "req_9", retryAfter: TimeSpan.FromSeconds(3)),
            ("retry-after", "3"),
            ("x-request-id", "req_9"));

        Assert.Equal(new CheckResult("E4", CheckOutcome.Pass, "Every 429 met, 1 in all, had its Retry-After and request id read."), ConformanceChecks.E4([outcome]));
    }

    [Fact]
    public void E4_RetryAfterMsWins_AndAMismatchFails()
    {
        var outcome = Failed(
            ConformanceProbe.Noul,
            429,
            "{}",
            Error(DecisionErrorKind.RateLimited, 429, "{}", retryAfter: TimeSpan.FromSeconds(3)),
            ("retry-after", "3"),
            ("retry-after-ms", "1500"));

        Assert.Equal(
            new CheckResult("E4", CheckOutcome.Fail, "Noul: Retry-After asks 00:00:01.5000000, but DecisionError.RetryAfter is 00:00:03."),
            ConformanceChecks.E4([outcome]));
    }

    [Fact]
    public void E4_AMissedRequestId_Fails()
    {
        var outcome = Failed(ConformanceProbe.Noul, 429, "{}", Error(DecisionErrorKind.RateLimited, 429, "{}"), ("x-typesafe-request-id", "req_7"));

        Assert.Equal("Noul: x-typesafe-request-id is req_7, but DecisionError.RequestId is null.", ConformanceChecks.E4([outcome]).Detail);
    }

    [Fact]
    public void E4_AnOversizedRetryAfterMs_IsClampedLikeTheCore()
    {
        var outcome = Failed(
            ConformanceProbe.Noul,
            429,
            "{}",
            Error(DecisionErrorKind.RateLimited, 429, "{}", retryAfter: TimeSpan.FromMilliseconds(int.MaxValue)),
            ("retry-after-ms", "99999999999999999999999999"));

        Assert.Equal(CheckOutcome.Pass, ConformanceChecks.E4([outcome]).Outcome);
    }
}
