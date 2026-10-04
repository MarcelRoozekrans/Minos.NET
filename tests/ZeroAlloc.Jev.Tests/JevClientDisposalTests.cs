using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Transport;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Tests;

/// <summary>
/// Disposing a client while a call is in flight: a request it tears down returns <see cref="JevErrorKind.Disposed"/>
/// and is never retried; a call started after disposal throws; a borrowed <see cref="HttpClient"/> is left alone.
/// </summary>
/// <remarks>
/// Every order is forced with a handler that blocks until released, never with a sleep. In the telemetry collection,
/// because one test listens to the ZeroAlloc.Jev source.
/// </remarks>
[Collection(TelemetryListeners.Name)]
public sealed class JevClientDisposalTests
{
    // Disposing the owned HttpClient cancels the token the handler waits on, so the handler fails with
    // TaskCanceledException, as a real handler does. That reached the caller as Timeout, and the retry proxy retried it.
    [Fact]
    public async Task InFlightEvaluate_TornDownByDispose_IsDisposed_AfterOneAttempt()
    {
        var gate = new Gate(Success);
        var client = Owned(gate.Handler);

        var call = client.EvaluateAsync(Request()).AsTask();
        var result = await DisposeWhileBlocked(gate, client, call);

        AssertDisposed(result);
        Assert.IsAssignableFrom<OperationCanceledException>(result.Error.Exception);
        _ = ClientTestKit.OnlyRequest(gate.Handler);
    }

    [Fact]
    public async Task InFlightTypedEvaluate_TornDownByDispose_IsDisposed_AfterOneAttempt_AndReturnsTheBuffers()
    {
        var gate = new Gate(Success);
        var pool = new CountingPool();
        var client = Owned(gate.Handler, pool: pool);

        var call = client.EvaluateAsync<UrgencyCheck>("Help!").AsTask();
        var result = await DisposeWhileBlocked(gate, client, call);

        AssertDisposed(result);
        _ = ClientTestKit.OnlyRequest(gate.Handler);
        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task InFlightBuiltSetEvaluate_TornDownByDispose_IsDisposed_AfterOneAttempt_AndReturnsTheBuffers()
    {
        var gate = new Gate(Success);
        var pool = new CountingPool();
        var client = Owned(gate.Handler, pool: pool);

        var call = client.EvaluateAsync(OneNoulSet(), "Help!").AsTask();
        var result = await DisposeWhileBlocked(gate, client, call);

        AssertDisposed(result);
        _ = ClientTestKit.OnlyRequest(gate.Handler);
        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task InFlightListModels_TornDownByDispose_IsDisposed_AfterOneAttempt()
    {
        var gate = new Gate(Success);
        var client = Owned(gate.Handler);

        var call = client.ListModelsAsync().AsTask();
        var result = await DisposeWhileBlocked(gate, client, call);

        AssertDisposed(result);
        _ = ClientTestKit.OnlyRequest(gate.Handler);
    }

    // The other way a disposal meets a call in flight: the attempt fails on its own with a transient error, here a 503
    // the handler returns after the disposal, and the retry it earns would go to the disposed HttpClient, which throws
    // ObjectDisposedException. The retry is not sent, and the call returns Disposed.
    [Fact]
    public async Task DisposeBeforeATransientFailure_StopsItsRetry_AndIsDisposed()
    {
        var gate = new Gate(() => Status(HttpStatusCode.ServiceUnavailable), honoursCancellation: false);
        var client = Owned(gate.Handler);

        var call = client.EvaluateAsync(Request()).AsTask();
        var result = await DisposeWhileBlocked(gate, client, call);

        AssertDisposed(result);
        Assert.Null(result.Error.Exception);
        _ = ClientTestKit.OnlyRequest(gate.Handler);
    }

    // The order the other way round: the per-attempt time-out fires and the failure is mapped before Dispose runs.
    [Fact]
    public async Task RealTimeoutBeforeDispose_IsStillTimeout()
    {
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                timedOut.TrySetResult();
            }

            return Success();
        });
        var client = Owned(handler, maxRetries: 0, timeout: TimeSpan.FromMilliseconds(100));

        var call = client.EvaluateAsync(Request()).AsTask();
        await timedOut.Task;
        var result = await call;
        client.Dispose();

        Assert.Equal(JevErrorKind.Timeout, result.Error.Kind);
        _ = ClientTestKit.OnlyRequest(handler);
    }

    [Fact]
    public async Task EveryCallStartedAfterDispose_OnAnOwnedClient_Throws()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        var client = Owned(handler);
        client.Dispose();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(Request()));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync<UrgencyCheck>("Help!"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.EvaluateAsync(OneNoulSet(), "Help!"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.ListModelsAsync());
        Assert.Empty(handler.Requests);
        Assert.True(handler.Disposed);
    }

    // Disposing the client leaves a borrowed HttpClient alone, so nothing is torn down: the call runs to completion,
    // its retry included, and the HttpClient still works afterwards.
    [Fact]
    public async Task InFlightCall_OverABorrowedHttpClient_IsUnaffectedByDispose()
    {
        var attempts = 0;
        var gate = new Gate(() => ++attempts == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Success());
        using var http = new HttpClient(gate.Handler);
        var client = new JevClient(Settings(maxRetries: 2), http, ownedHandler: null, TimeProvider.System);

        var call = client.EvaluateAsync(Request()).AsTask();
        var result = await DisposeWhileBlocked(gate, client, call);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, gate.Handler.Requests.Count);
        Assert.False(gate.Handler.Disposed);
        using var response = await http.GetAsync(new Uri("https://api.typesafe.ai/v1/models"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TornDownCall_LogsDisposedOnce_AndNoRetriedAttempt()
    {
        using var logs = new LogCapture();
        var gate = new Gate(Success);
        var client = Owned(gate.Handler, logger: logs.Factory.CreateLogger(JevLog.Category));

        var call = client.EvaluateAsync(Request()).AsTask();
        AssertDisposed(await DisposeWhileBlocked(gate, client, call));

        Assert.DoesNotContain(1003, logs.EventIds);
        Assert.Equal(nameof(JevErrorKind.Disposed), LogAssert.Field(logs.Only(1002), "ErrorKind"));
    }

    [Fact]
    public async Task TornDownCall_SpanCarriesDisposedAsItsErrorType()
    {
        using var capture = new TelemetryCapture();
        var gate = new Gate(Success);
        var client = Owned(gate.Handler);

        var call = client.EvaluateAsync(Request()).AsTask();
        AssertDisposed(await DisposeWhileBlocked(gate, client, call));

        var span = capture.Span();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("Disposed", span.GetTagItem("error.type"));
    }

    private static async Task<Result<T, JevError>> DisposeWhileBlocked<T>(Gate gate, JevClient client, Task<Result<T, JevError>> call)
    {
        await gate.Entered.Task;
        client.Dispose();
        gate.Release();
        return await call;
    }

    private static void AssertDisposed<T>(Result<T, JevError> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Disposed, result.Error.Kind);
        Assert.Equal("The client was disposed while the request was in flight.", result.Error.Message);
        Assert.Null(result.Error.StatusCode);
    }

    // An owned client over the handler, with two retries and a short backoff unless told otherwise.
    private static JevClient Owned(
        StubHandler handler, int maxRetries = 2, TimeSpan? timeout = null, CountingPool? pool = null, ILogger? logger = null)
        => new(Settings(maxRetries, timeout), httpClient: null, handler, TimeProvider.System, pool ?? new CountingPool(), logger);

    private static JevClientSettings Settings(int maxRetries, TimeSpan? timeout = null)
        => JevClientSettings.Resolve(
            new JevClientOptions
            {
                ApiKey = "test-key",
                Model = ClientTestKit.TestModel,
                MaxRetries = maxRetries,
                InitialBackoff = TimeSpan.FromMilliseconds(5),
                Jitter = false,
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
            },
            _ => null);

    private static SystemOneRequest Request()
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), JevJsonContext.Default.SystemOneRequest)!;

    private static JevQuestionSet OneNoulSet()
    {
        var built = JevQuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out _).Build();
        Assert.True(built.IsSuccess);
        return built.Value;
    }

    private static HttpResponseMessage Success()
        => new(HttpStatusCode.OK) { Content = new StringContent(Fixture.Text("response-noul.json"), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status)
        => new(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    /// <summary>A handler whose first attempt blocks until <see cref="Release"/>; later attempts pass straight through.</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Gate(Func<HttpResponseMessage> respond, bool honoursCancellation = true)
            => Handler = new StubHandler(async (_, ct) =>
            {
                Entered.TrySetResult();

                // A real handler gives up when the HttpClient cancels the request; one that does not still answers.
                if (honoursCancellation)
                {
                    await _released.Task.WaitAsync(ct);
                }
                else
                {
                    await _released.Task;
                }

                return respond();
            });

        public StubHandler Handler { get; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _released.TrySetResult();
    }
}
