using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Transport;

namespace ZeroAlloc.Jev.Tests;

public sealed class JevClientRetryTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(529)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(408)]
    public async Task RetryableStatus_IsRetried_UntilSuccess(int status)
    {
        var handler = StubHandler.Sequence(() => Response(status), () => Response(status), Success);
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailure_IsRetried()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => ++calls <= 2
            ? throw new HttpRequestException("connection refused")
            : Task.FromResult(Success()));
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ClientTimeout_IsRetried()
    {
        var calls = 0;
        var handler = new StubHandler(async (_, ct) =>
        {
            if (++calls <= 2)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return Success();
        });
        using var client = new JevClient(Settings(timeout: TimeSpan.FromMilliseconds(400)), httpClient: null, handler, TimeProvider.System);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task NonRetryableStatus_IsReturnedAfterOneAttempt(int status)
    {
        var handler = StubHandler.Sequence(() => Response(status), Success);
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsFailure);
        Assert.Equal(status, result.Error.StatusCode);
        AssertOneAttempt(handler);
    }

    [Fact]
    public async Task InvalidSuccessBody_IsNotRetried()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, "not json");
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        AssertOneAttempt(handler);
    }

    [Fact]
    public async Task ExhaustedRetries_ReturnTheLastError()
    {
        var handler = StubHandler.Sequence(() => Response(503), () => Response(503), () => Response(529));
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsFailure);
        Assert.Equal(529, result.Error.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ZeroRetries_MakeOneAttempt()
    {
        var handler = StubHandler.Sequence(() => Response(503), Success);
        using var client = Client(handler, maxRetries: 0);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(JevErrorKind.Overloaded, result.Error.Kind);
        AssertOneAttempt(handler);
    }

    [Fact]
    public async Task ListModels_IsRetriedToo()
    {
        var handler = StubHandler.Sequence(() => Response(429), () => Json(HttpStatusCode.OK, Fixture.Text("models.json")));
        using var client = Client(handler);

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    // The spec (and TypeSafe's Python SDK) calls for the header to be absent on the first attempt. ZeroAlloc.Rest
    // 2.2.0 cannot do that yet: a null [Header] argument still sends an empty-valued header instead of omitting it
    // (ZeroAlloc-Net/ZeroAlloc.Rest#354, closed upstream but not yet in a released package). Once a release with
    // that fix ships and this project upgrades to it, FirstAttemptRetryCount below becomes null with no code change
    // on this side, and these two constants should be updated together with it.
    private const string FirstAttemptRetryCount = "";

    [Fact]
    public async Task RetryCount_SentOnRetries()
    {
        var handler = StubHandler.Sequence(() => Response(503), () => Response(503), Success);
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal([FirstAttemptRetryCount, "1", "2"], handler.Requests.Select(r => r.RetryCount));
    }

    [Fact]
    public async Task RetryCount_CarriesNoAttemptNumber_WhenTheFirstAttemptSucceeds()
    {
        var handler = StubHandler.Sequence(Success);
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(FirstAttemptRetryCount, handler.Requests[0].RetryCount);
    }

    [Fact]
    public async Task ListModels_RetryCount_SentOnRetries()
    {
        var handler = StubHandler.Sequence(() => Response(503), () => Response(503), () => Json(HttpStatusCode.OK, Fixture.Text("models.json")));
        using var client = Client(handler);

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal([FirstAttemptRetryCount, "1", "2"], handler.Requests.Select(r => r.RetryCount));
    }

    [Fact]
    public async Task RetryAfter_ReplacesTheBackoff()
    {
        var handler = StubHandler.Sequence(() => Response(429, retryAfterMs: "150"), Success);
        using var client = Client(handler);

        _ = await client.EvaluateAsync(Request());

        Assert.True(Gap(handler, 0) >= TimeSpan.FromMilliseconds(130), $"waited {Gap(handler, 0)}");
    }

    [Fact]
    public async Task RetryAfter_IsCappedByMaxRetryDelay()
    {
        var handler = StubHandler.Sequence(() => Response(429, retryAfter: "30"), Success);
        using var client = Client(handler, maxRetryDelay: TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        _ = await client.EvaluateAsync(Request());

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"waited {stopwatch.Elapsed}");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Backoff_Doubles()
    {
        var handler = StubHandler.Sequence(() => Response(503), () => Response(503), Success);
        using var client = Client(handler, initialBackoff: TimeSpan.FromMilliseconds(60));

        _ = await client.EvaluateAsync(Request());

        Assert.True(Gap(handler, 0) >= TimeSpan.FromMilliseconds(50), $"first wait {Gap(handler, 0)}");
        Assert.True(Gap(handler, 1) >= TimeSpan.FromMilliseconds(100), $"second wait {Gap(handler, 1)}");
    }

    [Fact]
    public async Task CancellationDuringAWait_ThrowsPromptly()
    {
        // Cancellation starts only once the 429 is being returned, so it always lands in the 20 s wait that follows,
        // never before the first request or during it. MaxRetryDelay is raised so the wait is not capped below 10 s.
        using var cancellation = new CancellationTokenSource();
        var handler = StubHandler.Sequence(
            () =>
            {
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
                return Response(429, retryAfter: "20");
            },
            Success);
        using var client = Client(handler, maxRetryDelay: TimeSpan.FromSeconds(30));

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(Request(), cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        AssertOneAttempt(handler);
    }

    [Fact]
    public async Task ProgrammingError_SurfacesUnwrapped()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("bug"));
        using var client = Client(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.EvaluateAsync(Request()));

        Assert.Equal("bug", exception.Message);
        AssertOneAttempt(handler);
    }

    [Fact]
    public async Task ProgrammingError_InListModels_SurfacesUnwrapped()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("bug"));
        using var client = Client(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ListModelsAsync());

        Assert.Equal("bug", exception.Message);
        AssertOneAttempt(handler);
    }

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    private JevClient Client(
        StubHandler handler,
        int maxRetries = 2,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxRetryDelay = null)
    {
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        return new JevClient(
            Settings(maxRetries: maxRetries, initialBackoff: initialBackoff, maxRetryDelay: maxRetryDelay),
            http,
            ownedHandler: null,
            TimeProvider.System);
    }

    private static JevClientSettings Settings(
        int maxRetries = 2,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxRetryDelay = null,
        TimeSpan? timeout = null)
        => JevClientSettings.Resolve(
            new JevClientOptions
            {
                ApiKey = "test-key",
                MaxRetries = maxRetries,
                InitialBackoff = initialBackoff ?? TimeSpan.FromMilliseconds(5),
                MaxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(5),
                Jitter = false,
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
            },
            _ => null);

    private static void AssertOneAttempt(StubHandler handler)
    {
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        _ = Assert.Single(handler.Requests);
#pragma warning restore HLQ005
    }

    private static TimeSpan Gap(StubHandler handler, int index)
        => Stopwatch.GetElapsedTime(handler.Requests[index].Timestamp, handler.Requests[index + 1].Timestamp);

    private static HttpResponseMessage Success() => Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Response(int status, string? retryAfter = null, string? retryAfterMs = null)
    {
        var response = Json((HttpStatusCode)status, "{}");
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        if (retryAfterMs is not null)
        {
            response.Headers.TryAddWithoutValidation("retry-after-ms", retryAfterMs);
        }

        return response;
    }

    private static SystemOneRequest Request()
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), JevJsonContext.Default.SystemOneRequest)!;
}
