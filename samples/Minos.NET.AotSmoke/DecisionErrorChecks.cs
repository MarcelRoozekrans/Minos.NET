using System.Net;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="DecisionError"/>'s public constructor under Native AOT, as a test double or a caller's own error uses it, and
/// the request id an HTTP error carries.
/// </summary>
internal static class DecisionErrorChecks
{
    [Covers("Minos.DecisionError.DecisionError(Minos.DecisionErrorKind kind, string! message) -> void")]
    public static void HandBuiltErrorCarriesItsKindAndMessage()
    {
        var error = new DecisionError(DecisionErrorKind.Timeout, "The call took too long.");

        Program.Check(
            error.Kind == DecisionErrorKind.Timeout
                && string.Equals(error.Message, "The call took too long.", StringComparison.Ordinal)
                && error.StatusCode is null
                && error.Failures.Count == 0
                && string.Equals(error.ToString(), "Timeout: The call took too long.", StringComparison.Ordinal),
            "new DecisionError(kind, message) carries its kind and message and nothing else under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentNullException>(() => _ = new DecisionError(DecisionErrorKind.Timeout, null!)),
            "new DecisionError rejects a null message under Native AOT");
    }

    [Covers("Minos.DecisionError.DecisionError(Minos.DecisionErrorKind kind, string! message, System.Collections.Generic.IReadOnlyList<Minos.QuestionFailure!>! failures) -> void")]
    public static void HandBuiltRefusalNamesItsQuestions()
    {
        var error = new DecisionError(DecisionErrorKind.Refused, "The model refused.", [new QuestionFailure("refusal", "is_urgent", "The model refused.")]);

        Program.Check(
            error.Kind == DecisionErrorKind.Refused
                && error.Failures.Count == 1
                && string.Equals(error.Failures[0].QuestionKey, "is_urgent", StringComparison.Ordinal),
            "new DecisionError(kind, message, failures) lists its failures under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentNullException>(() => _ = new DecisionError(DecisionErrorKind.Refused, "The model refused.", null!)),
            "new DecisionError rejects null failures under Native AOT");
    }

    public static async Task HttpErrorCarriesTheRequestId()
    {
        using var http = new HttpClient(new CannedHandler(
            HttpStatusCode.Unauthorized,
            """{"detail":{"error_type":"authentication_error","message":"Cannot authenticate with the server."}}""",
            ("x-typesafe-request-id", "req_smoke")))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new SystemOne.SystemOneDecisionClient(http, Program.Options());

        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);

        Program.Check(
            result.IsFailure
                && result.Error.Kind == DecisionErrorKind.Unauthorized
                && string.Equals(result.Error.RequestId, "req_smoke", StringComparison.Ordinal)
                && string.Equals(result.Error.ToString(), "Unauthorized (401, request req_smoke): The API returned HTTP 401.", StringComparison.Ordinal),
            "a 401's x-typesafe-request-id is DecisionError.RequestId and is in its ToString under Native AOT");
    }
}
