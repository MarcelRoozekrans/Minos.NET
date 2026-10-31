using System.Collections.Concurrent;
using System.Diagnostics;
using ZeroAlloc.Results;

namespace Minos.Recording.Tests;

/// <summary>
/// A shadow candidate scripted with a <see cref="TaskCompletionSource{TResult}"/>: each call notes the request, the
/// token, and the <see cref="Activity.Current"/> and <see cref="DecisionRecording.Current"/> it ran with, then waits
/// until <see cref="Release"/> or <see cref="Throw"/>, or answers at once when it was made with <c>held: false</c>. A
/// cancelled token ends the wait with <see cref="OperationCanceledException"/>, unless it was made with
/// <c>ignoresCancellation: true</c>.
/// </summary>
internal sealed class ShadowCandidate(bool held = true, bool ignoresCancellation = false, DecisionClientMetadata? metadata = null) : IDecisionClient
{
    internal static readonly DecisionClientMetadata CandidateMetadata = new("anthropic", new Uri("https://candidate.example.test/"), "candidate-default");

    private readonly TaskCompletionSource<Func<Result<DecisionResponse, DecisionError>>> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Call> _calls = new();
    private readonly DecisionClientMetadata _metadata = metadata ?? CandidateMetadata;
    private int _finished;

    /// <summary>Gets the calls the candidate saw, in order.</summary>
    public IReadOnlyList<Call> Calls => [.. _calls];

    /// <summary>Gets how many calls started and have not returned or thrown yet.</summary>
    public int Pending => _calls.Count - Volatile.Read(ref _finished);

    /// <summary>Gets how many calls returned or threw.</summary>
    public int Finished => Volatile.Read(ref _finished);

    /// <summary>A success with the candidate's own model, id and usage.</summary>
    public static DecisionResponse Answer() => new(
        TestRecords.Definition(),
        [QuestionAnswer.Noul(0.2), QuestionAnswer.Choice(0, 0.6, [0.6, 0.4])],
        model: "candidate-served",
        usage: new DecisionUsage { InputTokens = 130, OutputTokens = 12 })
    {
        Id = "resp-shadow",
        UpstreamProvider = "Bedrock",
    };

    /// <summary>Waits until the candidate was called <paramref name="count"/> times.</summary>
    public async Task WaitStartedAsync(int count = 1)
    {
        await _started.Task.WaitAsync(Stage.Bound);
        var deadline = Stopwatch.StartNew();
        while (_calls.Count < count)
        {
            Assert.True(deadline.Elapsed < Stage.Bound, $"The candidate was called {_calls.Count} times, not {count}.");
            await Task.Delay(5);
        }
    }

    /// <summary>Lets every held call answer with <paramref name="result"/>, or with <see cref="Answer"/>.</summary>
    public void Release(Result<DecisionResponse, DecisionError>? result = null)
    {
        var answer = result ?? Result<DecisionResponse, DecisionError>.Success(Answer());
        _gate.TrySetResult(() => answer);
    }

    /// <summary>Makes every held call throw <paramref name="exception"/>.</summary>
    public void Throw(Exception exception) => _gate.TrySetResult(() => throw exception);

    public async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        _calls.Enqueue(new Call(request, Activity.Current, DecisionRecording.Current, cancellationToken));
        _started.TrySetResult();
        try
        {
            if (!held)
            {
                return Result<DecisionResponse, DecisionError>.Success(Answer());
            }

            var answer = ignoresCancellation
                ? await _gate.Task.ConfigureAwait(false)
                : await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return answer();
        }
        finally
        {
            Interlocked.Increment(ref _finished);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType == typeof(DecisionClientMetadata) ? _metadata : null;

    public void Dispose()
    {
    }

    /// <summary>One call the candidate saw.</summary>
    internal sealed record Call(DecisionRequest Request, Activity? Activity, DecisionRecording.ScopeState? Scope, CancellationToken Token);
}
