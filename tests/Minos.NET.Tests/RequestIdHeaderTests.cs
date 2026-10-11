using Minos.Transport;

namespace Minos.Tests;

public sealed class RequestIdHeaderTests
{
    [Fact]
    public void Names_AreTriedInThisOrder()
        => Assert.Equal(["x-request-id", "x-typesafe-request-id", "request-id"], RequestIdHeader.Names);

    [Theory]
    [InlineData("x-request-id", "req_openai")]
    [InlineData("x-typesafe-request-id", "req_typesafe")]
    [InlineData("request-id", "req_plain")]
    [InlineData("X-TypeSafe-Request-Id", "req_typesafe")]
    public void EachHeader_IsRead_InAnyLetterCase(string name, string value)
        => Assert.Equal(value, RequestIdHeader.Read(Headers((name, value))));

    [Fact]
    public void XRequestId_WinsOverTheOthers_WhateverTheirOrder()
        => Assert.Equal("req_first", RequestIdHeader.Read(Headers(("request-id", "req_third"), ("x-typesafe-request-id", "req_second"), ("x-request-id", "req_first"))));

    [Fact]
    public void XTypeSafeRequestId_WinsOverRequestId()
        => Assert.Equal("req_second", RequestIdHeader.Read(Headers(("request-id", "req_third"), ("x-typesafe-request-id", "req_second"))));

    [Fact]
    public void ABlankValue_FallsThroughToTheNextName()
        => Assert.Equal("req_plain", RequestIdHeader.Read(Headers(("x-request-id", "  "), ("request-id", " req_plain "))));

    [Fact]
    public void OtherHeaders_AreNeverRead()
        => Assert.Null(RequestIdHeader.Read(Headers(("CF-RAY", "8c1f2a3b4c5d6e7f-AMS"), ("x-openrouter-id", "gen-1"), ("Retry-After", "3"))));

    [Fact]
    public void NoHeaders_GiveNull()
        => Assert.Null(RequestIdHeader.Read(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)));

    private static Dictionary<string, IReadOnlyList<string>> Headers(params (string Name, string Value)[] headers)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (name, value) in headers)
        {
            map[name] = [value];
        }

        return map;
    }
}
