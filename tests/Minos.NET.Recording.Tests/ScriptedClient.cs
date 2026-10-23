using System.Collections.Concurrent;
using ZeroAlloc.Results;

namespace Minos.Recording.Tests;

/// <summary>
/// An inner client that answers from a script, then with <see cref="Stage.Answer"/>. It counts its calls, keeps the
/// requests it saw, offers the given metadata, and runs <paramref name="during"/> inside each call.
/// </summary>
internal sealed class ScriptedClient(DecisionClientMetadata? metadata = null, Func<DecisionRequest, ValueTask>? during = null) : IDecisionClient
{
    private readonly ConcurrentQueue<Result<DecisionResponse, DecisionError>> _script = new();
    private readonly ConcurrentQueue<DecisionRequest> _requests = new();
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public IReadOnlyList<DecisionRequest> Requests => [.. _requests];

    public ScriptedClient Then(Result<DecisionResponse, DecisionError> result)
    {
        _script.Enqueue(result);
        return this;
    }

    public ScriptedClient ThenFail(DecisionErrorKind kind, string message = "x")
        => Then(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, message)));

    public async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        _requests.Enqueue(request);
        if (during is not null)
        {
            await during(request);
        }

        return _script.TryDequeue(out var next) ? next : Result<DecisionResponse, DecisionError>.Success(Stage.Answer());
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && metadata is not null && serviceType == typeof(DecisionClientMetadata) ? metadata : null;

    public void Dispose()
    {
    }
}
