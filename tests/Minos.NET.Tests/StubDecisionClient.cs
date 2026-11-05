using System.Collections.Concurrent;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>
/// A client that answers each request with a delegate, records the requests it saw, and counts its disposals; for the
/// routing stages' tests.
/// </summary>
internal sealed class StubDecisionClient(Func<DecisionRequest, CancellationToken, ValueTask<Result<DecisionResponse, DecisionError>>> evaluate)
    : IDecisionClient
{
    private int _disposals;

    public ConcurrentQueue<DecisionRequest> Requests { get; } = new();

    public int Calls => Requests.Count;

    public int Disposals => Volatile.Read(ref _disposals);

    /// <summary>A client that answers every request with the answers for its definition's questions, looked up by key.</summary>
    public static StubDecisionClient Answering(IReadOnlyDictionary<string, QuestionAnswer> answers, string? model = null, DecisionUsage? usage = null)
        => Responding(request => Respond(request.Definition, answers, model, usage));

    /// <summary>A client that answers every request with the response <paramref name="respond"/> makes.</summary>
    public static StubDecisionClient Responding(Func<DecisionRequest, DecisionResponse> respond)
        => new((request, _) => new(Result<DecisionResponse, DecisionError>.Success(respond(request))));

    /// <summary>A client that fails every request with <paramref name="error"/>.</summary>
    public static StubDecisionClient Failing(DecisionError error)
        => new((_, _) => new(Result<DecisionResponse, DecisionError>.Failure(error)));

    /// <summary>A client that fails every request with an error of <paramref name="kind"/>.</summary>
    public static StubDecisionClient Failing(DecisionErrorKind kind) => Failing(new DecisionError(kind, "failed"));

    /// <summary>A response with the answer for each of the definition's questions, looked up by key.</summary>
    public static DecisionResponse Respond(QuestionSetDefinition definition, IReadOnlyDictionary<string, QuestionAnswer> answers, string? model = null, DecisionUsage? usage = null)
        => new(definition, [.. definition.Questions.Select(q => answers[q.Key])], model, usage);

    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        return evaluate(request, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => Interlocked.Increment(ref _disposals);
}
