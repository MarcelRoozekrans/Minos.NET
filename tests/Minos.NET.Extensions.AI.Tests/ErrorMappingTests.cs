using System.ClientModel;
using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI.Tests;

public sealed class ErrorMappingTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static readonly DecisionContent State = DecisionContent.FromString("I was charged twice.");

    private static readonly QuestionDefinition Refund = QuestionDefinition.Noul("refund", "Does the customer ask for a refund?");

    private static readonly QuestionDefinition Team = QuestionDefinition.Choice(
        "team", "Which team handles it?", new OptionDefinition("billing", null), new OptionDefinition("shipping", null));

    [Theory]
    [InlineData(401, DecisionErrorKind.Unauthorized)]
    [InlineData(403, DecisionErrorKind.Unauthorized)]
    [InlineData(400, DecisionErrorKind.Validation)]
    [InlineData(422, DecisionErrorKind.Validation)]
    [InlineData(429, DecisionErrorKind.RateLimited)]
    [InlineData(503, DecisionErrorKind.Overloaded)]
    [InlineData(529, DecisionErrorKind.Overloaded)]
    [InlineData(500, DecisionErrorKind.Server)]
    [InlineData(404, DecisionErrorKind.Http)]
    public async Task AStatus_MapsToItsKind(int status, DecisionErrorKind kind)
    {
        var thrown = FakePipelineResponse.Exception(status);

        var error = await FailWith(thrown);

        Assert.Equal(kind, error.Kind);
        Assert.Equal(status, error.StatusCode);
        Assert.Same(thrown, error.Exception);
        Assert.Null(error.RetryAfter);
        Assert.Null(error.RequestId);
    }

    [Fact]
    public async Task RetryAfter_PrefersRetryAfterMs()
    {
        var error = await FailWith(FakePipelineResponse.Exception(429, headers: [("Retry-After", "7"), ("retry-after-ms", "1500")]));

        Assert.Equal(DecisionErrorKind.RateLimited, error.Kind);
        Assert.Equal(429, error.StatusCode);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), error.RetryAfter);
    }

    [Fact]
    public async Task RetryAfter_ReadsRetryAfter_WithoutRetryAfterMs()
    {
        var error = await FailWith(FakePipelineResponse.Exception(429, headers: ("Retry-After", "7")));

        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }

    [Fact]
    public async Task RetryAfter_FallsBackToRetryAfter_WhenRetryAfterMsIsUnreadable()
    {
        var error = await FailWith(FakePipelineResponse.Exception(429, headers: [("retry-after-ms", "soon"), ("Retry-After", "7")]));

        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }

    [Fact]
    public async Task RequestId_IsReadFromTheHeaders()
    {
        var error = await FailWith(FakePipelineResponse.Exception(500, headers: [("request-id", "req-other"), ("x-request-id", "req-123")]));

        Assert.Equal("req-123", error.RequestId);
    }

    [Fact]
    public async Task TheMessage_KeepsTheProvidersTextOut()
    {
        var thrown = FakePipelineResponse.Exception(400, "SECRET provider text that may echo the state");

        var error = await FailWith(thrown);

        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
        Assert.Contains("400", error.Message, StringComparison.Ordinal);
        Assert.Same(thrown, error.Exception);
    }

    [Fact]
    public async Task AJsonBody_IsTheDetail_AndStaysOutOfTheMessage()
    {
        var body = """{"error":{"message":"SECRET body text","code":"invalid"}}""";
        var response = new FakePipelineResponse(422, ("Content-Type", "application/json; charset=utf-8")) { Body = BinaryData.FromString(body) };

        var error = await FailWith(new ClientResultException("The provider failed.", response));

        Assert.Equal(DecisionErrorKind.Validation, error.Kind);
        Assert.NotNull(error.Detail);
        Assert.Equal(body, error.Detail.Value.GetRawText());
        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("text/plain", "SECRET gateway text")]
    [InlineData("application/json", "SECRET not json")]
    [InlineData(null, """{"error":"no content type"}""")]
    public async Task ABodyThatIsNotJson_GivesNoDetail(string? contentType, string body)
    {
        (string, string)[] headers = contentType is null ? [] : [("Content-Type", contentType)];
        var response = new FakePipelineResponse(502, headers) { Body = BinaryData.FromString(body) };

        var error = await FailWith(new ClientResultException("The provider failed.", response));

        Assert.Equal(DecisionErrorKind.Server, error.Kind);
        Assert.Null(error.Detail);
        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClientResultException_WithoutAResponse_IsNetwork()
    {
        var inner = new HttpRequestException("Connection refused.");
        var thrown = new ClientResultException("Connection refused.", response: null, inner);

        var error = await FailWith(thrown);

        Assert.Equal(DecisionErrorKind.Network, error.Kind);
        Assert.Null(error.StatusCode);
        Assert.Same(thrown, error.Exception);
    }

    [Fact]
    public async Task AnHttpRequestException_IsNetwork()
    {
        var thrown = new HttpRequestException("SECRET connection failure.");

        var error = await FailWith(thrown);

        Assert.Equal(DecisionErrorKind.Network, error.Kind);
        Assert.Same(thrown, error.Exception);
        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATimeoutException_IsTimeout()
    {
        var thrown = new TimeoutException();

        var error = await FailWith(thrown);

        Assert.Equal(DecisionErrorKind.Timeout, error.Kind);
        Assert.Same(thrown, error.Exception);
    }

    [Fact]
    public async Task ACancellation_ThatTheCallerDidNotAskFor_FromATimeout_IsTimeout()
    {
        // HttpClient reports its own time-out as a cancellation with a TimeoutException inside.
        var thrown = new TaskCanceledException("The request timed out.", new TimeoutException());

        var error = await FailWith(thrown);

        Assert.Equal(DecisionErrorKind.Timeout, error.Kind);
        Assert.Same(thrown, error.Exception);
    }

    [Fact]
    public async Task AnyOtherException_Propagates()
    {
        using var client = Throwing(new InvalidOperationException("A bug.")).AsDecisionClient();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State)));

        Assert.Equal("A bug.", thrown.Message);
    }

    [Fact]
    public async Task AFailure_CancelsTheOtherCalls()
    {
        // The failing call waits until the other one hangs, so the failure always has a call in flight to cancel.
        var hanging = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hangingCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient(async (messages, _, cancellationToken) =>
        {
            if (messages[1].Text.StartsWith("Question: Which team", StringComparison.Ordinal))
            {
                hanging.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    hangingCancelled.SetResult();
                    throw;
                }
            }

            await hanging.Task.WaitAsync(Bound, cancellationToken);
            throw FakePipelineResponse.Exception(429);
        });
        using var client = chat.AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund, Team), State)).AsTask().WaitAsync(Bound);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.RateLimited, result.Error.Kind);
        Assert.True(hangingCancelled.Task.IsCompleted);
    }

    [Fact]
    public async Task AnInvalidAnswer_CancelsTheOtherCalls()
    {
        // The failing call waits until the other one hangs, so the failure always has a call in flight to cancel.
        var hanging = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hangingCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient(async (messages, _, cancellationToken) =>
        {
            if (messages[1].Text.StartsWith("Question: Which team", StringComparison.Ordinal))
            {
                hanging.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    hangingCancelled.SetResult();
                    throw;
                }
            }

            await hanging.Task.WaitAsync(Bound, cancellationToken);
            return ChatCompletions.WithoutLogprobs();
        });
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund, Team), State)).AsTask().WaitAsync(Bound);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.True(hangingCancelled.Task.IsCompleted);
    }

    [Fact]
    public async Task ABug_InOneCall_PropagatesAndCancelsTheOthers()
    {
        // The failing call waits until the other one hangs, so the failure always has a call in flight to cancel.
        var hanging = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hangingCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient(async (messages, _, cancellationToken) =>
        {
            if (messages[1].Text.StartsWith("Question: Which team", StringComparison.Ordinal))
            {
                hanging.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    hangingCancelled.SetResult();
                    throw;
                }
            }

            await hanging.Task.WaitAsync(Bound, cancellationToken);
            throw new InvalidOperationException("A bug.");
        });
        using var client = chat.AsDecisionClient();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund, Team), State)).AsTask().WaitAsync(Bound));
        Assert.True(hangingCancelled.Task.IsCompleted);
    }

    [Fact]
    public async Task CallerCancellation_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient(async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        using var client = chat.AsDecisionClient();

        var call = client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund, Team), State), cancellation.Token).AsTask();
        await started.Task.WaitAsync(Bound);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Bound));
    }

    [Fact]
    public async Task CallerCancellation_Throws_EvenWhenTheChatClientReportsItAsAMappedFailure()
    {
        // A chat client may surface the caller's cancellation as a transport failure; the caller still gets the cancellation.
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient(async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                throw new HttpRequestException("Aborted.", exception);
            }

            throw new InvalidOperationException("Unreachable.");
        });
        using var client = chat.AsDecisionClient();

        var call = client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State), cancellation.Token).AsTask();
        await started.Task.WaitAsync(Bound);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Bound));
    }

    [Fact]
    public async Task CallerCancellation_Throws_EvenWhenTheChatClientReportsItAsATimeout()
    {
        // HttpClient's own time-out looks like this; once the caller has cancelled, the cancellation wins.
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new ScriptedChatClient(async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException("The request timed out.", new TimeoutException());
            }

            throw new InvalidOperationException("Unreachable.");
        });
        using var client = chat.AsDecisionClient();

        var call = client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State), cancellation.Token).AsTask();
        await started.Task.WaitAsync(Bound);
        await cancellation.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Bound));
        Assert.IsType<TimeoutException>(thrown.InnerException);
    }

    [Fact]
    public async Task AnUnmappedException_AfterCallerCancellation_PropagatesUnchanged()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bug = new InvalidOperationException("A bug.");
        var chat = new ScriptedChatClient(async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw bug;
            }

            throw new InvalidOperationException("Unreachable.");
        });
        using var client = chat.AsDecisionClient();

        var call = client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State), cancellation.Token).AsTask();
        await started.Task.WaitAsync(Bound);
        await cancellation.CancelAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => call.WaitAsync(Bound));
        Assert.Same(bug, thrown);
    }

    private static async Task<DecisionError> FailWith(Exception exception)
    {
        using var client = Throwing(exception).AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Refund), State));

        Assert.True(result.IsFailure);
        return result.Error;
    }

    private static ScriptedChatClient Throwing(Exception exception)
        => new((IReadOnlyList<ChatMessage> _, ChatOptions? _) => throw exception);
}
