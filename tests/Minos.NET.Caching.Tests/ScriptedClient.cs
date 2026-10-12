using ZeroAlloc.Results;

namespace Minos.Caching.Tests;

/// <summary>An inner client that answers from a script, counts its calls and offers the given metadata.</summary>
internal sealed class ScriptedClient(DecisionClientMetadata? metadata = null) : IDecisionClient
{
    private readonly Queue<Result<DecisionResponse, DecisionError>> _script = new();

    public int Calls { get; private set; }

    public ScriptedClient Then(Result<DecisionResponse, DecisionError> result)
    {
        _script.Enqueue(result);
        return this;
    }

    public ScriptedClient ThenAnswer() => Then(Result<DecisionResponse, DecisionError>.Success(Responses.Answer()));

    public ScriptedClient ThenFail(DecisionErrorKind kind)
        => Then(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, "x")));

    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return new(_script.Count > 0 ? _script.Dequeue() : Result<DecisionResponse, DecisionError>.Success(Responses.Answer()));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && metadata is not null && serviceType == typeof(DecisionClientMetadata) ? metadata : null;

    public void Dispose()
    {
    }
}
