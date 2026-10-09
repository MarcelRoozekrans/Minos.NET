using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Minos.Tests;

/// <summary>
/// Compiles every constructor call shape callers use, so a new overload that makes one ambiguous breaks this build.
/// <c>new JevClient(null, null)</c> is the one shape that no longer compiles: CS0121, since both two-parameter
/// overloads accept two null literals and nullability takes no part in overload resolution. It always threw before.
/// </summary>
public sealed class JevClientConstructorShapeTests : IDisposable
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
        JevClientOptions? options = new() { ApiKey = "test-key" };
        ILoggerFactory factory = NullLoggerFactory.Instance;
        var http = Http();

        // Compiled, not run: the value of this test is that it compiles. The shapes without options read
        // TYPESAFE_API_KEY, which a test machine need not set.
        _ = new Func<JevClient>[]
        {
            () => new JevClient(),
            () => new JevClient(options),
            () => new JevClient(http),
            () => new JevClient(http, options),
            () => new JevClient(http, null),
            () => new JevClient(null!, options),
            () => new JevClient(options, factory),
            () => new JevClient(options, null),
            () => new JevClient(null, factory),
            () => new JevClient(http, options, factory),
            () => new JevClient(http, options, null),
        };
    }

    [Fact]
    public void ShapesWithAKey_Construct()
    {
        JevClientOptions? options = new() { ApiKey = "test-key" };
        ILoggerFactory factory = NullLoggerFactory.Instance;

        Func<JevClient>[] shapes =
        [
            () => new JevClient(options),
            () => new JevClient(Http(), options),
            () => new JevClient(options, factory),
            () => new JevClient(options, null),
            () => new JevClient(Http(), options, factory),
            () => new JevClient(Http(), options, null),
        ];

        foreach (var create in shapes)
        {
            Assert.Null(Record.Exception(() => create().Dispose()));
        }
    }

    [Fact]
    public void LoggingConstructor_OverANullHttpClient_Throws()
        => Assert.Throws<ArgumentNullException>(() => new JevClient(null!, new JevClientOptions { ApiKey = "k" }, NullLoggerFactory.Instance));

    private HttpClient Http()
    {
        var http = new HttpClient(StubHandler.Json(HttpStatusCode.OK, "{}"));
        _httpClients.Add(http);
        return http;
    }
}
