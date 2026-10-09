using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Minos.Tests;

/// <summary>
/// Compiles every constructor call shape callers use, so a new overload that makes one ambiguous breaks this build.
/// <c>new DecisionClient(null, null)</c> is the one shape that no longer compiles: CS0121, since both two-parameter
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
        DecisionClientOptions? options = new() { ApiKey = "test-key" };
        ILoggerFactory factory = NullLoggerFactory.Instance;
        var http = Http();

        // Compiled, not run: the value of this test is that it compiles. The shapes without options read
        // TYPESAFE_API_KEY, which a test machine need not set.
        _ = new Func<DecisionClient>[]
        {
            () => new DecisionClient(),
            () => new DecisionClient(options),
            () => new DecisionClient(http),
            () => new DecisionClient(http, options),
            () => new DecisionClient(http, null),
            () => new DecisionClient(null!, options),
            () => new DecisionClient(options, factory),
            () => new DecisionClient(options, null),
            () => new DecisionClient(null, factory),
            () => new DecisionClient(http, options, factory),
            () => new DecisionClient(http, options, null),
        };
    }

    [Fact]
    public void ShapesWithAKey_Construct()
    {
        DecisionClientOptions? options = new() { ApiKey = "test-key" };
        ILoggerFactory factory = NullLoggerFactory.Instance;

        Func<DecisionClient>[] shapes =
        [
            () => new DecisionClient(options),
            () => new DecisionClient(Http(), options),
            () => new DecisionClient(options, factory),
            () => new DecisionClient(options, null),
            () => new DecisionClient(Http(), options, factory),
            () => new DecisionClient(Http(), options, null),
        ];

        foreach (var create in shapes)
        {
            Assert.Null(Record.Exception(() => create().Dispose()));
        }
    }

    [Fact]
    public void LoggingConstructor_OverANullHttpClient_Throws()
        => Assert.Throws<ArgumentNullException>(() => new DecisionClient(null!, new DecisionClientOptions { ApiKey = "k" }, NullLoggerFactory.Instance));

    private HttpClient Http()
    {
        var http = new HttpClient(StubHandler.Json(HttpStatusCode.OK, "{}"));
        _httpClients.Add(http);
        return http;
    }
}
