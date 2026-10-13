using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Minos.SystemOne;

namespace Minos.Tests;

/// <summary>
/// Compiles every constructor call shape callers use, so a new overload that makes one ambiguous breaks this build.
/// <c>new SystemOneDecisionClient(null, null)</c> is the one shape that no longer compiles: CS0121, since both two-parameter
/// overloads accept two null literals and nullability takes no part in overload resolution. It always threw before.
/// </summary>
public sealed class DecisionClientConstructorShapeTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public void EveryCallShape_Compiles()
    {
        SystemOneDecisionClientOptions? options = new() { ApiKey = "test-key" };
        ILoggerFactory factory = NullLoggerFactory.Instance;
        var http = Http();

        // Compiled, not run: the value of this test is that it compiles. The shapes without options read
        // TYPESAFE_API_KEY, which a test machine need not set.
        _ = new Func<SystemOneDecisionClient>[]
        {
            () => new SystemOneDecisionClient(),
            () => new SystemOneDecisionClient(options),
            () => new SystemOneDecisionClient(http),
            () => new SystemOneDecisionClient(http, options),
            () => new SystemOneDecisionClient(http, null),
            () => new SystemOneDecisionClient(null!, options),
            () => new SystemOneDecisionClient(options, factory),
            () => new SystemOneDecisionClient(options, null),
            () => new SystemOneDecisionClient(null, factory),
            () => new SystemOneDecisionClient(http, options, factory),
            () => new SystemOneDecisionClient(http, options, null),
        };
    }

    [Fact]
    public void ShapesWithAKey_Construct()
    {
        SystemOneDecisionClientOptions? options = new() { ApiKey = "test-key" };
        ILoggerFactory factory = NullLoggerFactory.Instance;

        Func<SystemOneDecisionClient>[] shapes =
        [
            () => new SystemOneDecisionClient(options),
            () => new SystemOneDecisionClient(Http(), options),
            () => new SystemOneDecisionClient(options, factory),
            () => new SystemOneDecisionClient(options, null),
            () => new SystemOneDecisionClient(Http(), options, factory),
            () => new SystemOneDecisionClient(Http(), options, null),
        ];

        foreach (var create in shapes)
        {
            Assert.Null(Record.Exception(() => create().Dispose()));
        }
    }

    [Fact]
    public void LoggingConstructor_OverANullHttpClient_Throws()
        => Assert.Throws<ArgumentNullException>(() => new SystemOneDecisionClient(null!, new SystemOneDecisionClientOptions { ApiKey = "k" }, NullLoggerFactory.Instance));

    private HttpClient Http()
    {
        var http = new HttpClient(StubHandler.Json(HttpStatusCode.OK, "{}"));
        _httpClients.Add(http);
        return http;
    }
}
