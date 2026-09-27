using System.Net;
using System.Text;
using System.Text.Json;
using ZeroAlloc.Jev.Transport;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Jev.Tests;

public sealed class JevErrorMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly JevErrorMapper Mapper = new(new FixedTimeProvider(Now));

    [Theory]
    [InlineData(401, JevErrorKind.Unauthorized)]
    [InlineData(403, JevErrorKind.Unauthorized)]
    [InlineData(400, JevErrorKind.Validation)]
    [InlineData(422, JevErrorKind.Validation)]
    [InlineData(429, JevErrorKind.RateLimited)]
    [InlineData(503, JevErrorKind.Overloaded)]
    [InlineData(529, JevErrorKind.Overloaded)]
    [InlineData(500, JevErrorKind.Server)]
    [InlineData(502, JevErrorKind.Server)]
    [InlineData(404, JevErrorKind.Http)]
    [InlineData(409, JevErrorKind.Http)]
    public void Status_MapsToKind_AndKeepsStatusCode(int status, JevErrorKind expected)
    {
        var error = Mapper.Map(Status(status));

        Assert.Equal(expected, error.Kind);
        Assert.Equal(status, error.StatusCode);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_MapsToTimeout_WithException()
    {
        var cause = new TaskCanceledException("timed out");

        var error = Mapper.Map(new HttpError((HttpStatusCode)0, Headers(), cause.Message) { Kind = HttpErrorKind.Timeout, Exception = cause });

        Assert.Equal(JevErrorKind.Timeout, error.Kind);
        Assert.Null(error.StatusCode);
        Assert.Same(cause, error.Exception);
    }

    [Fact]
    public void Transport_MapsToNetwork_WithException()
    {
        var cause = new HttpRequestException("connection refused");

        var error = Mapper.Map(new HttpError((HttpStatusCode)0, Headers(), cause.Message) { Kind = HttpErrorKind.Transport, Exception = cause });

        Assert.Equal(JevErrorKind.Network, error.Kind);
        Assert.Null(error.StatusCode);
        Assert.Equal("connection refused", error.Message);
        Assert.Same(cause, error.Exception);
    }

    [Fact]
    public void Deserialization_MapsToInvalidResponse_KeepingStatus()
    {
        var cause = new JsonException("bad json");

        var error = Mapper.Map(new HttpError(HttpStatusCode.OK, Headers(), cause.Message) { Kind = HttpErrorKind.Deserialization, Exception = cause });

        Assert.Equal(JevErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(200, error.StatusCode);
        Assert.Same(cause, error.Exception);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", 0)]
    public void RetryAfter_DeltaSeconds_IsParsed(string header, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), Mapper.Map(Status(429, retryAfter: header)).RetryAfter);

    [Fact]
    public void RetryAfter_HttpDate_IsRelativeToNow()
        => Assert.Equal(TimeSpan.FromSeconds(30), Mapper.Map(Status(429, retryAfter: "Sun, 27 Sep 2026 12:00:30 GMT")).RetryAfter);

    [Fact]
    public void RetryAfter_PastDate_IsZero()
        => Assert.Equal(TimeSpan.Zero, Mapper.Map(Status(503, retryAfter: "Sun, 27 Sep 2026 11:00:00 GMT")).RetryAfter);

    [Theory]
    [InlineData(null)]
    [InlineData("soon")]
    [InlineData("-3")]
    public void RetryAfter_AbsentOrInvalid_IsNull(string? header)
        => Assert.Null(Mapper.Map(Status(429, retryAfter: header)).RetryAfter);

    [Fact]
    public void RetryAfter_HeaderNameIsCaseInsensitive()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>> { ["retry-after"] = ["4"] };

        var error = Mapper.Map(new HttpError(HttpStatusCode.TooManyRequests, headers, null));

        Assert.Equal(TimeSpan.FromSeconds(4), error.RetryAfter);
    }

    [Theory]
    [InlineData("Sunday, 27-Sep-26 12:00:30 GMT")]
    [InlineData("Sun Sep 27 12:00:30 2026")]
    [InlineData("sun, 27 sep 2026 12:00:30 gmt")]
    public void RetryAfter_OtherHttpDateForms_AreParsed(string header)
        => Assert.Equal(TimeSpan.FromSeconds(30), Mapper.Map(Status(429, retryAfter: header)).RetryAfter);

    [Fact]
    public void RetryAfter_DeltaSecondsOverflow_IsClamped()
        => Assert.Equal(RetryAfterHeader.MaxDelay, Mapper.Map(Status(429, retryAfter: "99999999999999999999")).RetryAfter);

    [Theory]
    [InlineData("1500", 1500)]
    [InlineData("250.5", 250.5)]
    [InlineData("0", 0)]
    public void RetryAfterMs_IsParsed(string header, double milliseconds)
        => Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), Mapper.Map(WithHeaders(429, ("retry-after-ms", header))).RetryAfter);

    [Fact]
    public void RetryAfterMs_WinsOverRetryAfter()
        => Assert.Equal(TimeSpan.FromMilliseconds(200), Mapper.Map(WithHeaders(429, ("Retry-After", "5"), ("retry-after-ms", "200"))).RetryAfter);

    [Theory]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("NaN")]
    public void RetryAfterMs_Invalid_FallsBackToRetryAfter(string header)
        => Assert.Equal(TimeSpan.FromSeconds(5), Mapper.Map(WithHeaders(429, ("Retry-After", "5"), ("retry-after-ms", header))).RetryAfter);

    private static HttpError WithHeaders(int status, params (string Name, string Value)[] headers)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers)
        {
            map[name] = [value];
        }

        return new HttpError((HttpStatusCode)status, map, null) { Kind = HttpErrorKind.Status };
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/problem+json")]
    public void Detail_JsonBody_IsParsed(string contentType)
    {
        var error = Mapper.Map(Status(422, body: """{"detail":"questions.x.instructions is required"}""", contentType: contentType));

        Assert.NotNull(error.Detail);
        Assert.Equal("questions.x.instructions is required", error.Detail.Value.GetProperty("detail").GetString());
    }

    [Fact]
    public void Detail_NonJsonBody_IsNull()
        => Assert.Null(Mapper.Map(Status(422, body: "bad request", contentType: "text/plain")).Detail);

    [Fact]
    public void Detail_TruncatedBody_IsNull()
        => Assert.Null(Mapper.Map(Status(422, body: """{"detail":"x"}""", contentType: "application/json", truncated: true)).Detail);

    [Fact]
    public void Detail_MalformedJson_IsNull()
        => Assert.Null(Mapper.Map(Status(422, body: "{not json", contentType: "application/json")).Detail);

    [Fact]
    public void ToString_WithStatus_IncludesKindAndStatus()
        => Assert.StartsWith("RateLimited (429): ", Mapper.Map(Status(429)).ToString(), StringComparison.Ordinal);

    [Fact]
    public void ToString_WithoutStatus_IncludesKind()
        => Assert.Equal("Unsupported: not here", new JevError(JevErrorKind.Unsupported, "not here").ToString());

    private static HttpError Status(
        int status,
        string? retryAfter = null,
        string? body = null,
        string? contentType = null,
        bool truncated = false)
    {
        var headers = retryAfter is null
            ? Headers()
            : new Dictionary<string, IReadOnlyList<string>> { ["Retry-After"] = [retryAfter] };

        return new HttpError((HttpStatusCode)status, headers, null)
        {
            Kind = HttpErrorKind.Status,
            Body = body is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(body),
            ContentType = contentType,
            BodyTruncated = truncated,
        };
    }

    private static Dictionary<string, IReadOnlyList<string>> Headers() => [];
}
