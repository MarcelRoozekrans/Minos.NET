using Minos.OpenAI.Protocols;

namespace Minos.OpenAI.Tests;

public sealed class OpenAIErrorsTests
{
    private static ReadOnlySpan<byte> OpenAIBody => """{"error":{"message":"Bad model","type":"invalid_request_error","param":"model","code":null}}"""u8;

    [Theory]
    [InlineData(400, DecisionErrorKind.Validation)]
    [InlineData(401, DecisionErrorKind.Unauthorized)]
    [InlineData(403, DecisionErrorKind.Unauthorized)]
    [InlineData(404, DecisionErrorKind.Http)]
    [InlineData(422, DecisionErrorKind.Validation)]
    [InlineData(429, DecisionErrorKind.RateLimited)]
    [InlineData(500, DecisionErrorKind.Server)]
    [InlineData(503, DecisionErrorKind.Overloaded)]
    [InlineData(529, DecisionErrorKind.Overloaded)]
    public void Status_MapsToKind_WithTheGenericMessage(int status, DecisionErrorKind kind)
    {
        var error = OpenAIDecisionsProtocol.Instance.MapError(status, default, bodyTruncated: false, contentType: null, retryAfter: TimeSpan.FromSeconds(3), requestId: null);

        Assert.Equal(kind, error.Kind);
        Assert.Equal($"The API returned HTTP {status}.", error.Message);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(3), error.RetryAfter);
        Assert.Null(error.Detail);
    }

    [Fact]
    public void OpenAIErrorBody_GivesItsMessage_AndTheWholeBodyAsDetail()
    {
        var error = OpenAIDecisionsProtocol.Instance.MapError(400, OpenAIBody, false, "application/json", null, null);

        Assert.Equal(DecisionErrorKind.Validation, error.Kind);
        Assert.Equal("Bad model", error.Message);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(OpenAIBody), error.Detail!.Value.GetRawText());
        Assert.Equal("model", error.Detail.Value.GetProperty("error").GetProperty("param").GetString());
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("text/plain", false)]
    [InlineData(null, false)]
    public void TruncatedOrNonJsonBody_GivesTheGenericMessage_AndNoDetail(string? contentType, bool truncated)
    {
        var error = OpenAIDecisionsProtocol.Instance.MapError(400, OpenAIBody, truncated, contentType, null, null);

        Assert.Equal("The API returned HTTP 400.", error.Message);
        Assert.Null(error.Detail);
    }

    [Theory]
    [InlineData("{oops")]
    [InlineData("""{"error":{"message":"Bad model"}} x""")]
    public void InvalidJsonBody_GivesTheGenericMessage_AndNoDetail(string body)
    {
        var error = OpenAIDecisionsProtocol.Instance.MapError(400, System.Text.Encoding.UTF8.GetBytes(body), false, "application/json", null, null);

        Assert.Equal("The API returned HTTP 400.", error.Message);
        Assert.Null(error.Detail);
    }

    [Theory]
    [InlineData("""{"error":"bad"}""")]
    [InlineData("""{"error":{"type":"server_error"}}""")]
    [InlineData("""{"error":{"message":""}}""")]
    [InlineData("""{"error":{"message":5}}""")]
    [InlineData("""{"message":"Bad model"}""")]
    [InlineData("""["Bad model"]""")]
    public void JsonBodyWithoutAnErrorMessage_GivesTheGenericMessage_AndTheBodyAsDetail(string body)
    {
        var error = OpenAIDecisionsProtocol.Instance.MapError(503, System.Text.Encoding.UTF8.GetBytes(body), false, "application/json", null, null);

        Assert.Equal(DecisionErrorKind.Overloaded, error.Kind);
        Assert.Equal("The API returned HTTP 503.", error.Message);
        Assert.Equal(body, error.Detail!.Value.GetRawText());
    }

    [Fact]
    public void TheRequestId_IsKept()
        => Assert.Equal(
            "req_abc",
            OpenAIDecisionsProtocol.Instance.MapError(401, OpenAIBody, false, "application/json", null, "req_abc").RequestId);
}
