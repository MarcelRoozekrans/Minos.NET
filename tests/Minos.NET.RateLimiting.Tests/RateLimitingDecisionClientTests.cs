using System.Threading.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;

namespace Minos.RateLimiting.Tests;

public sealed class RateLimitingDecisionClientTests
{
    private static readonly QuestionSetDefinition Definition = new(QuestionDefinition.Noul("is_urgent", "Is this urgent?"));

    private static readonly DecisionRequest Request = new(Definition, "s");

    private static Result<DecisionResponse, DecisionError> Ok()
        => Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(Definition, [QuestionAnswer.Noul(0.5)]));

    private static Result<DecisionResponse, DecisionError> Fail()
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "x") { StatusCode = 500 });

    /// <summary>An inner client that answers each call from a script, or holds it until <see cref="Held"/>.</summary>
    private sealed class Inner : IDecisionClient
    {
        private readonly Queue<Func<ValueTask<Result<DecisionResponse, DecisionError>>>> _script = new();

        public int Calls { get; private set; }

        public bool Disposed { get; private set; }

        public TaskCompletionSource<Result<DecisionResponse, DecisionError>> Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Inner Then(Result<DecisionResponse, DecisionError> result)
        {
            _script.Enqueue(() => new(result));
            return this;
        }

        public Inner ThenHold()
        {
            _script.Enqueue(() => new(Held.Task));
            return this;
        }

        public Inner ThenFaulted()
        {
            _script.Enqueue(() => new(Task.FromException<Result<DecisionResponse, DecisionError>>(new InvalidOperationException("boom"))));
            return this;
        }

        public Inner ThenThrow()
        {
            _script.Enqueue(() => throw new InvalidOperationException("boom"));
            return this;
        }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _script.Count > 0 ? _script.Dequeue()() : new(Ok());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Disposed = true;
    }

    private static ConcurrencyLimiter Concurrency(int permits, int queue = 0)
        => new(new ConcurrencyLimiterOptions { PermitLimit = permits, QueueLimit = queue, QueueProcessingOrder = QueueProcessingOrder.OldestFirst });

    [Fact]
    public async Task A_granted_call_goes_to_the_inner_client()
    {
        using var limiter = Concurrency(1);
        var inner = new Inner();
        using var stage = new RateLimitingDecisionClient(inner, limiter);

        var result = await stage.EvaluateAsync(Request);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_rejected_call_is_RateLimited_with_no_status_and_is_never_sent()
    {
        using var limiter = Concurrency(1);
        var inner = new Inner().ThenHold();
        using var stage = new RateLimitingDecisionClient(inner, limiter);
        var first = stage.EvaluateAsync(Request).AsTask();

        var second = await stage.EvaluateAsync(Request);

        Assert.True(second.IsFailure);
        Assert.Equal(DecisionErrorKind.RateLimited, second.Error.Kind);
        Assert.Null(second.Error.StatusCode);
        Assert.Equal(RateLimitingDecisionClient.RejectedMessage, second.Error.Message);
        Assert.Equal(1, inner.Calls);
        inner.Held.SetResult(Ok());
        await first;
    }

    [Fact]
    public async Task A_rejection_carries_the_limiters_retry_after()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
            Window = TimeSpan.FromSeconds(10),
            AutoReplenishment = false,
        });
        using var stage = new RateLimitingDecisionClient(new Inner(), limiter);
        await stage.EvaluateAsync(Request);

        var rejected = await stage.EvaluateAsync(Request);

        Assert.Equal(DecisionErrorKind.RateLimited, rejected.Error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(10), rejected.Error.RetryAfter);
    }

    [Fact]
    public async Task A_queued_call_waits_for_the_permit_and_then_goes()
    {
        using var limiter = Concurrency(1, queue: 1);
        var inner = new Inner().ThenHold();
        using var stage = new RateLimitingDecisionClient(inner, limiter);
        var first = stage.EvaluateAsync(Request).AsTask();

        var second = stage.EvaluateAsync(Request).AsTask();

        Assert.False(second.IsCompleted);
        Assert.Equal(1, inner.Calls);
        inner.Held.SetResult(Ok());
        await first;
        Assert.True((await second).IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("throw")]
    [InlineData("async")]
    public async Task The_permit_is_released_when_the_call_ends(string outcome)
    {
        using var limiter = Concurrency(1);
        var inner = outcome switch
        {
            "success" => new Inner().Then(Ok()),
            "failure" => new Inner().Then(Fail()),
            "throw" => new Inner().ThenThrow(),
            _ => new Inner().ThenHold(),
        };
        using var stage = new RateLimitingDecisionClient(inner, limiter);

        if (string.Equals(outcome, "throw", StringComparison.Ordinal))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await stage.EvaluateAsync(Request));
        }
        else if (string.Equals(outcome, "async", StringComparison.Ordinal))
        {
            var held = stage.EvaluateAsync(Request).AsTask();
            Assert.Equal(0, limiter.GetStatistics()!.CurrentAvailablePermits);
            inner.Held.SetResult(Ok());
            await held;
        }
        else
        {
            await stage.EvaluateAsync(Request);
        }

        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public async Task The_permit_is_released_when_the_inner_call_faults_after_the_stage_returned()
    {
        using var limiter = Concurrency(1);
        var inner = new Inner().ThenHold();
        using var stage = new RateLimitingDecisionClient(inner, limiter);
        var held = stage.EvaluateAsync(Request).AsTask();

        inner.Held.SetException(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => held);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public async Task The_permit_is_released_when_the_inner_call_completed_already_faulted()
    {
        using var limiter = Concurrency(1);
        var inner = new Inner().ThenFaulted();
        using var stage = new RateLimitingDecisionClient(inner, limiter);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await stage.EvaluateAsync(Request));

        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public async Task The_permit_is_released_when_the_inner_call_is_cancelled_in_flight()
    {
        using var limiter = Concurrency(1);
        var inner = new Inner().ThenHold();
        using var stage = new RateLimitingDecisionClient(inner, limiter);
        var held = stage.EvaluateAsync(Request).AsTask();

        inner.Held.SetCanceled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public async Task The_permit_is_released_when_a_queued_then_granted_call_throws()
    {
        using var limiter = Concurrency(1, queue: 1);
        var inner = new Inner().ThenHold().ThenThrow();
        using var stage = new RateLimitingDecisionClient(inner, limiter);
        var first = stage.EvaluateAsync(Request).AsTask();
        var queued = stage.EvaluateAsync(Request).AsTask();
        Assert.False(queued.IsCompleted);

        inner.Held.SetResult(Ok());
        await first;

        await Assert.ThrowsAsync<InvalidOperationException>(() => queued);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public async Task Cancelling_a_queued_call_throws_and_sends_nothing()
    {
        using var limiter = Concurrency(1, queue: 1);
        var inner = new Inner().ThenHold();
        using var stage = new RateLimitingDecisionClient(inner, limiter);
        var first = stage.EvaluateAsync(Request).AsTask();
        using var cancel = new CancellationTokenSource();

        var queued = stage.EvaluateAsync(Request, cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, inner.Calls);
        inner.Held.SetResult(Ok());
        await first;
    }

    [Fact]
    public void Disposing_the_stage_disposes_the_inner_client_but_not_the_limiter()
    {
        using var limiter = Concurrency(1);
        var inner = new Inner();
        var stage = new RateLimitingDecisionClient(inner, limiter);

        stage.Dispose();

        Assert.True(inner.Disposed);
        using var lease = limiter.AttemptAcquire(1);
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task UseRateLimiting_adds_the_stage_and_a_retry_stage_outside_it_retries_a_rejection()
    {
        var time = new FakeTimeProvider();
        using var limiter = Concurrency(1);
        var inner = new Inner().ThenHold();
        using var client = inner.AsBuilder()
            .Use(next => new RetryingDecisionClient(next, new DecisionRetryOptions { Jitter = false }, timeProvider: time))
            .UseRateLimiting(limiter)
            .Build();
        var first = client.EvaluateAsync(Request).AsTask();

        // The one permit is held, so the second call is rejected and the retry stage waits on the fake clock.
        var second = client.EvaluateAsync(Request).AsTask();
        Assert.False(second.IsCompleted);
        Assert.Equal(1, inner.Calls);
        inner.Held.SetResult(Ok());
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(30));
        var result = await second.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.NotNull(client.GetService(typeof(RateLimitingDecisionClient)));
    }

    [Fact]
    public async Task A_call_after_Dispose_throws_even_when_the_limiter_has_no_permit()
    {
        using var limiter = Concurrency(1);
        var inner = new Inner();
        var stage = new RateLimitingDecisionClient(inner, limiter);
        using var held = limiter.AttemptAcquire(1);
        Assert.True(held.IsAcquired);
        var rejected = await stage.EvaluateAsync(Request);
        Assert.Equal(DecisionErrorKind.RateLimited, rejected.Error.Kind);

        stage.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await stage.EvaluateAsync(Request));
    }

    [Fact]
    public async Task A_call_on_a_disposed_limiter_throws_ObjectDisposedException()
    {
        var limiter = Concurrency(1);
        using var stage = new RateLimitingDecisionClient(new Inner(), limiter);
        limiter.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await stage.EvaluateAsync(Request));
    }

    [Fact]
    public void The_constructor_and_UseRateLimiting_refuse_null()
    {
        using var limiter = Concurrency(1);

        Assert.Throws<ArgumentNullException>(() => new RateLimitingDecisionClient(new Inner(), null!));
        Assert.Throws<ArgumentNullException>(() => new Inner().AsBuilder().UseRateLimiting(null!));
        Assert.Throws<ArgumentNullException>(() => RateLimitingDecisionClientBuilderExtensions.UseRateLimiting(null!, limiter));
    }
}
