using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Minos.Serialization;
using Minos.Transport;

namespace Minos.Tests;

public sealed class DecisionClientRetryTests : IDisposable
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
        using var client = new DecisionClient(Settings(timeout: TimeSpan.FromMilliseconds(400)), httpClient: null, handler, TimeProvider.System);

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

        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
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
    public async Task ExhaustedRetries_On408_ReturnHttp()
    {
        // DecisionErrorKind.Http's docs: a 408 is retried, and returned as Http only after the retries are used up.
        var handler = StubHandler.Sequence(() => Response(408), () => Response(408), () => Response(408));
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Http, result.Error.Kind);
        Assert.Equal(408, result.Error.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ZeroRetries_MakeOneAttempt()
    {
        var handler = StubHandler.Sequence(() => Response(503), Success);
        using var client = Client(handler, maxRetries: 0);

        var result = await client.EvaluateAsync(Request());

        Assert.Equal(DecisionErrorKind.Overloaded, result.Error.Kind);
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

    [Fact]
    public async Task RetryCount_SentOnRetries()
    {
        var handler = StubHandler.Sequence(() => Response(503), () => Response(503), Success);
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal([null, "1", "2"], handler.Requests.Select(r => r.RetryCount));
    }

    [Fact]
    public async Task RetryCount_IsAbsent_WhenTheFirstAttemptSucceeds()
    {
        var handler = StubHandler.Sequence(Success);
        using var client = Client(handler);

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Null(handler.Requests[0].RetryCount);
    }

    [Fact]
    public async Task Typed_RetriesWithTheSameBody_AndSendsTheRetryCount()
    {
        var pool = new CountingPool();
        var outstandingPerAttempt = new List<int>();
        var responses = new Queue<Func<HttpResponseMessage>>([() => Response(503), Success]);
        var handler = new StubHandler((_, _) =>
        {
            outstandingPerAttempt.Add(pool.Outstanding);
            return Task.FromResult(responses.Dequeue()());
        });
        using var client = Client(handler, pool: pool);

        var result = await client.EvaluateAsync<UrgencyCheck>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
        Assert.Equal([null, "1"], handler.Requests.Select(r => r.RetryCount));
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);

        // The request body stays rented, and so readable, until the whole call completes.
        Assert.All(outstandingPerAttempt, outstanding => Assert.Equal(1, outstanding));
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task ListModels_RetryCount_SentOnRetries()
    {
        var handler = StubHandler.Sequence(() => Response(503), () => Response(503), () => Json(HttpStatusCode.OK, Fixture.Text("models.json")));
        using var client = Client(handler);

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal([null, "1", "2"], handler.Requests.Select(r => r.RetryCount));
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

    private DecisionClient Client(
        StubHandler handler,
        int maxRetries = 2,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxRetryDelay = null,
        CountingPool? pool = null)
    {
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        return new DecisionClient(
            Settings(maxRetries: maxRetries, initialBackoff: initialBackoff, maxRetryDelay: maxRetryDelay),
            http,
            ownedHandler: null,
            TimeProvider.System,
            pool ?? new CountingPool());
    }

    private static DecisionClientSettings Settings(
        int maxRetries = 2,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxRetryDelay = null,
        TimeSpan? timeout = null)
        => DecisionClientSettings.Resolve(
            new DecisionClientOptions
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
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;
}
